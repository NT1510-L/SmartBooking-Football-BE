using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Football_Booking.Repositories;

namespace Football_Booking.Services;

public sealed class BookingQueryService(BookingRepository repo, BookingService bookings)
{
    public async Task<IResult> ListBranches(string? search, string? area, int? page, int? size, CancellationToken ct)
    {
        var rows = repo.Branches.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(x => x.Name.Contains(search));
        if (!string.IsNullOrWhiteSpace(area)) rows = rows.Where(x => x.Area.Contains(area));
        return Results.Ok(await rows.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip(Offset(page, size)).Take(PageSize(size)).ToListAsync(ct));
    }

    public async Task<IResult> ListActivePitches(long id, CancellationToken ct) =>
        Results.Ok(await repo.Pitches.AsNoTracking().Where(x => x.BranchId == id && x.IsActive &&
            repo.Branches.Any(b => b.Id == id && b.IsActive)).OrderBy(x => x.PitchNumber).ToListAsync(ct));

    public async Task<IResult> Availability(long id, DateOnly date, CancellationToken ct)
    {
        if (date.Year is < 1000 or > 9998) return Results.BadRequest();
        var branch = await (from pitch in repo.Pitches join candidate in repo.Branches on pitch.BranchId equals candidate.Id
            where pitch.Id == id && pitch.IsActive && candidate.IsActive select candidate).SingleOrDefaultAsync(ct);
        if (branch is null) return Results.NotFound();
        var first = BookingService.ToUtc(date, 0);
        var next = first.AddDays(1);
        var now = DateTime.UtcNow;
        var occupied = await repo.Bookings.Where(x => x.PitchId == id && x.StartAtUtc >= first && x.StartAtUtc < next &&
            ((x.Status == "PendingPayment" && x.ExpiresAtUtc > now) || x.Status == "Confirmed" || x.Status == "Completed"))
            .Select(x => x.StartAtUtc).ToListAsync(ct);
        return Results.Ok(Enumerable.Range(branch.OpeningHour, branch.ClosingHour - branch.OpeningHour).Select(hour =>
        {
            var start = BookingService.ToUtc(date, hour);
            return new { hour, available = start > now && !occupied.Contains(start) };
        }));
    }

    public async Task<IResult> Quote(long id, DateOnly date, int hour, CancellationToken ct)
    {
        var branch = await repo.Branches.SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (branch is null) return Results.NotFound();
        if (hour < branch.OpeningHour || hour >= branch.ClosingHour || date.Year is < 1000 or > 9998) return Results.BadRequest();
        var quote = await bookings.Quote(id, date, hour, ct);
        return Results.Ok(new { quote.PolicyId, quote.Amount });
    }

    public async Task<IResult> ListBookings(ClaimsPrincipal actor, long? branchId, string? status, DateOnly? date, string? sort, int? page, int? size, CancellationToken ct)
    {
        var id = Id(actor);
        var query = from booking in repo.Bookings.AsNoTracking() join pitch in repo.Pitches on booking.PitchId equals pitch.Id
            select new { booking, pitch.BranchId, pitch.Name };
        if (actor.IsInRole("Manager"))
            query = query.Where(x => repo.Managers.Any(m => m.ManagerId == id && m.BranchId == x.BranchId));
        else if (!actor.IsInRole("Admin")) query = query.Where(x => x.booking.UserId == id);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.booking.Status == status);
        if (date.HasValue)
        {
            if (date.Value.Year is < 1000 or > 9998) return Results.BadRequest();
            var start = BookingService.ToUtc(date.Value, 0);
            var end = start.AddDays(1);
            query = query.Where(x => x.booking.StartAtUtc >= start && x.booking.StartAtUtc < end);
        }
        var ordered = sort == "asc" ? query.OrderBy(x => x.booking.StartAtUtc) : query.OrderByDescending(x => x.booking.StartAtUtc);
        return Results.Ok(await ordered.ThenBy(x => x.booking.Id).Skip(Offset(page, size)).Take(PageSize(size)).ToListAsync(ct));
    }

    public async Task<IResult> GetBooking(long id, ClaimsPrincipal actor, CancellationToken ct)
    {
        var booking = await repo.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (booking is null) return Results.NotFound();
        var own = booking.UserId == Id(actor);
        if (!own && !actor.IsInRole("Admin") &&
            (!actor.IsInRole("Manager") || !await repo.Manages(Id(actor), await repo.BranchOf(booking.PitchId, ct), ct)))
            return Results.Forbid();
        var payment = await repo.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == id, ct);
        return Results.Ok(new { booking, payment = payment is null ? null : bookings.PaymentDetails(payment) });
    }

    public async Task<IResult> ListNotifications(ClaimsPrincipal actor, int? page, int? size, CancellationToken ct) =>
        Results.Ok(await repo.Notifications.Where(x => x.UserId == Id(actor)).OrderByDescending(x => x.Id)
            .Skip(Offset(page, size)).Take(PageSize(size)).ToListAsync(ct));

    public async Task<IResult> ReadNotification(long id, ClaimsPrincipal actor, CancellationToken ct)
    {
        var count = await repo.Notifications.Where(x => x.Id == id && x.UserId == Id(actor))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, x => x.ReadAtUtc ?? DateTime.UtcNow), ct);
        return count == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static long Id(ClaimsPrincipal actor) => long.Parse(actor.FindFirstValue("sub")!);
    private static int PageSize(int? size) => Math.Clamp(size ?? 20, 1, 100);
    private static int Offset(int? page, int? size) => (Math.Clamp(page ?? 1, 1, 10000) - 1) * PageSize(size);
}
