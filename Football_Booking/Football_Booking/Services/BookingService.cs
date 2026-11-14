using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartBooking.Pricing;

using Football_Booking.Repositories;

namespace Football_Booking.Services;

public sealed record BookingInput(long PitchId, DateOnly Date, int Hour);
public sealed record SepayEvent(long Id, string? AccountNumber, string? Code, string? Content, string? TransferType, long TransferAmount);
public sealed record PaymentView(string Code, long AmountVnd, string Status, string? QrUrl, bool IsDemo);

public sealed class BookingService(BookingRepository repo, PricingService.PricingServiceClient pricing,
    IConfiguration config, ILogger<BookingService> logger)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private const string DemoAccount = "0000000000";

    public static DateTime ToUtc(DateOnly date, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0)), Zone);

    public async Task<PriceQuote> Quote(long branchId, DateOnly date, int hour, CancellationToken ct) =>
        await pricing.QuoteAsync(new QuoteRequest
        {
            BranchId = branchId, LocalDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Hour = hour
        }, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);

    public PaymentView PaymentDetails(Payment payment)
    {
        var account = config["Sepay:AccountNumber"];
        var bank = config["Sepay:Bank"];
        var isDemo = account == DemoAccount;
        var qr = payment.Status == "Pending" && !isDemo && !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(bank)
            ? $"https://vietqr.app/img?acc={Uri.EscapeDataString(account)}&bank={Uri.EscapeDataString(bank)}&amount={payment.AmountVnd}&des={Uri.EscapeDataString(payment.Code)}"
            : null;
        return new PaymentView(payment.Code, payment.AmountVnd, payment.Status, qr, isDemo);
    }

    public async Task<IResult> Create(long userId, BookingInput input, CancellationToken ct)
    {
        if (input.Hour is < 0 or > 23 || input.Date.Year < 1000 || input.Date.Year > 9998)
            return Results.BadRequest(new { message = "Khung giờ không hợp lệ." });
        if (string.IsNullOrWhiteSpace(config["Sepay:AccountNumber"]) ||
            string.IsNullOrWhiteSpace(config["Sepay:Bank"]) ||
            string.IsNullOrWhiteSpace(config["Sepay:WebhookKey"]))
            return Results.Problem("Hệ thống chưa cấu hình thanh toán.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var pitch = await repo.Pitches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.PitchId && x.IsActive, ct);
        if (pitch is null) return Results.NotFound();
        var branch = await repo.Branches.AsNoTracking().SingleAsync(x => x.Id == pitch.BranchId, ct);
        var start = ToUtc(input.Date, input.Hour);
        if (!branch.IsActive || input.Hour < branch.OpeningHour || input.Hour >= branch.ClosingHour || start <= DateTime.UtcNow)
            return Results.BadRequest(new { message = "Sân đóng cửa hoặc khung giờ đã qua." });
        var quote = await Quote(branch.Id, input.Date, input.Hour, ct);
        if (quote.Amount <= 0) return Results.Conflict(new { message = "Bảng giá chưa hỗ trợ thanh toán." });
        var now = DateTime.UtcNow;
        var expiry = now.AddMinutes(10);
        if (start < expiry) expiry = start;
        if (expiry <= now) return Results.Conflict(new { message = "Khung giờ đã qua." });

        await using var tx = await repo.BeginTransactionAsync(ct);
        var previousHoldId = await repo.Bookings.Where(x => x.PitchId == pitch.Id && x.StartAtUtc == start &&
            x.Status == "PendingPayment" && x.ExpiresAtUtc <= now)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (previousHoldId.HasValue)
            await repo.ExpireHold(await repo.Lock(previousHoldId.Value, ct), now, ct);

        var booking = new Booking
        {
            PitchId = pitch.Id, UserId = userId, StartAtUtc = start, ExpiresAtUtc = expiry,
            PriceVnd = quote.Amount, PricingPolicyId = quote.PolicyId
        };
        repo.Add(booking);
        await repo.SaveChangesAsync(ct);
        var payment = new Payment
        {
            BookingId = booking.Id, AmountVnd = quote.Amount, Code = "SB" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
        };
        repo.Add(payment);
        await repo.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Created pending booking {BookingId}", booking.Id);
        return Results.Created($"/api/bookings/{booking.Id}", new { booking, payment = PaymentDetails(payment) });
    }

    public async Task<IResult> Cancel(long id, long actorId, string role, CancellationToken ct)
    {
        await using var tx = await repo.BeginTransactionAsync(ct);
        var booking = await repo.Lock(id, ct);
        if (booking is null) return Results.NotFound();
        if (role != "Admin" && booking.UserId != actorId &&
            (role != "Manager" || !await repo.Manages(actorId, await repo.BranchOf(booking.PitchId, ct), ct)))
            return Results.Forbid();
        if (booking.Status == "Cancelled") return Results.NoContent();
        if (booking.Status != "PendingPayment" || booking.StartAtUtc <= DateTime.UtcNow)
            return Results.Conflict(new { message = "Chỉ hủy trực tiếp lịch đang chờ thanh toán." });
        booking.Status = "Cancelled";
        booking.CancelledAtUtc = DateTime.UtcNow;
        booking.CancelledByUserId = actorId;
        var payment = await repo.Payments.SingleAsync(x => x.BookingId == id, ct);
        payment.Status = "Cancelled";
        AddEvent(booking, "BookingCancelled");
        await repo.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> Receive(SepayEvent incoming, string authorization, CancellationToken ct)
    {
        var key = config["Sepay:WebhookKey"];
        var account = config["Sepay:AccountNumber"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(account))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorization), Encoding.UTF8.GetBytes($"Apikey {key}")))
            return Results.Unauthorized();
        if (incoming.Id <= 0 || incoming.TransferAmount <= 0) return Results.BadRequest();
        if (incoming.TransferType != "in" || incoming.AccountNumber != account)
            return Results.Ok(new { success = true });
        var code = System.Text.RegularExpressions.Regex.Match(
            (incoming.Code ?? "") + " " + (incoming.Content ?? ""), @"(?<![A-Z0-9])SB[A-F0-9]{16}(?![A-Z0-9])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Value.ToUpperInvariant();
        var candidate = await repo.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Code == code, ct);
        if (candidate is null) return Results.Ok(new { success = true });
        await using var tx = await repo.BeginTransactionAsync(ct);
        var booking = await repo.Lock(candidate.BookingId, ct);
        if (booking is null || await repo.Receipts.AnyAsync(x => x.SepayId == incoming.Id, ct))
            return Results.Ok(new { success = true });
        var payment = await repo.Payments.SingleAsync(x => x.Id == candidate.Id, ct);
        var now = DateTime.UtcNow;
        var outcome = "Closed";
        if (booking.Status == "PendingPayment" && booking.ExpiresAtUtc > now && payment.Status == "Pending")
        {
            outcome = incoming.TransferAmount == payment.AmountVnd ? "Accepted" : "AmountMismatch";
            if (outcome == "Accepted")
            {
                booking.Status = "Confirmed";
                payment.Status = "Paid";
                payment.PaidAtUtc = now;
                AddEvent(booking, "BookingConfirmed");
            }
        }
        repo.Add(new Receipt
        {
            SepayId = incoming.Id, PaymentId = payment.Id, AmountVnd = incoming.TransferAmount,
            Outcome = outcome, ReceivedAtUtc = now
        });
        await repo.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("SePay transaction {TransactionId} for booking {BookingId}: {Outcome}", incoming.Id, booking.Id, outcome);
        return Results.Ok(new { success = true });
    }

    private void AddEvent(Booking booking, string type) => repo.Add(new Outbox
    {
        BookingId = booking.Id, EventType = type, Payload = JsonSerializer.Serialize(new { booking.UserId })
    });
}
