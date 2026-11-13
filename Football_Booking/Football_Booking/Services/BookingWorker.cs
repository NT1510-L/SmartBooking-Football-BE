using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

using Football_Booking.Repositories;

namespace Football_Booking.Services;

public sealed class BookingWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<BookingWorker> logger) : BackgroundService
{
    private const string Stream = "football:bookings";
    private const string Group = "notifications";
    private readonly string consumer = Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        ConnectionMultiplexer? redis = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                try { await UpdateBookings(ct); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError(ex, "Booking maintenance failed"); }
                try
                {
                    redis ??= await ConnectionMultiplexer.ConnectAsync(config.GetConnectionString("Redis")
                        ?? throw new InvalidOperationException("Set ConnectionStrings__Redis."));
                    await ProcessStream(redis.GetDatabase(), ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError(ex, "Redis booking processing failed"); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        finally { redis?.Dispose(); }
    }

    private async Task UpdateBookings(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<BookingRepository>();
        var now = DateTime.UtcNow;
        var due = await repo.Bookings.Where(x => x.Status == "PendingPayment" && x.ExpiresAtUtc <= now)
            .OrderBy(x => x.ExpiresAtUtc).Select(x => x.Id).Take(50).ToListAsync(ct);
        foreach (var id in due)
        {
            await using var tx = await repo.BeginTransactionAsync(ct);
            if (!await repo.ExpireHold(await repo.Lock(id, ct), now, ct)) continue;
            await tx.CommitAsync(ct);
            logger.LogInformation("Expired booking {BookingId}", id);
        }
        var completed = await repo.Bookings.Where(x => x.Status == "Confirmed" && x.EndAtUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Completed"), ct);
        if (completed > 0) logger.LogInformation("Completed {Count} bookings", completed);
    }

    private async Task ProcessStream(IDatabase stream, CancellationToken ct)
    {
        try { await stream.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true); }
        catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal)) { }

        await using var scope = scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<BookingRepository>();
        var pending = await repo.Outbox.Where(x => x.PublishedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.EventId).Take(50).ToListAsync(ct);
        foreach (var row in pending)
        {
            await stream.StreamAddAsync(Stream, [
                new("event_id", row.EventId), new("booking_id", row.BookingId),
                new("type", row.EventType), new("payload", row.Payload)
            ]);
            row.PublishedAtUtc = DateTime.UtcNow;
            await repo.SaveChangesAsync(ct);
            logger.LogInformation("Published {EventType} {EventId}", row.EventType, row.EventId);
        }

        var recovered = await stream.StreamAutoClaimAsync(Stream, Group, consumer, 30000, "0-0", 50);
        var entries = await stream.StreamReadGroupAsync(Stream, Group, consumer, ">", 50);
        foreach (var entry in recovered.ClaimedEntries.Concat(entries))
        {
            var values = entry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value);
            using var payload = JsonDocument.Parse(values["payload"].ToString());
            var userId = payload.RootElement.GetProperty("UserId").GetInt64();
            var eventId = values["event_id"].ToString();
            var bookingId = (long)values["booking_id"];
            var message = values["type"] == "BookingConfirmed"
                ? $"Thanh toán thành công. Lịch đặt #{bookingId} đã xác nhận."
                : $"Lịch đặt #{bookingId} đã hủy.";
            await repo.AddNotification(userId, bookingId, eventId, message, ct);
            await stream.StreamAcknowledgeAsync(Stream, Group, entry.Id);
            logger.LogInformation("Consumed {EventId} for booking {BookingId}", eventId, bookingId);
        }
    }
}
