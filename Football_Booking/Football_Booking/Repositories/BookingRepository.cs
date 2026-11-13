using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Football_Booking.Repositories;

public sealed class BookingRepository(BookingDb db)
{
    public IQueryable<Account> Users => db.Users;
    public IQueryable<Branch> Branches => db.Branches;
    public IQueryable<Pitch> Pitches => db.Pitches;
    public IQueryable<Assignment> Managers => db.Managers;
    public IQueryable<Booking> Bookings => db.Bookings;
    public IQueryable<Payment> Payments => db.Payments;
    public IQueryable<Receipt> Receipts => db.Receipts;
    public IQueryable<Outbox> Outbox => db.Outbox;
    public IQueryable<Notice> Notifications => db.Notifications;

    public void Add<TEntity>(TEntity entity) where TEntity : class => db.Set<TEntity>().Add(entity);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => db.Database.BeginTransactionAsync(ct);
    public ValueTask<Branch?> FindBranch(long id, CancellationToken ct) => db.Branches.FindAsync([id], ct);
    public ValueTask<Pitch?> FindPitch(long id, CancellationToken ct) => db.Pitches.FindAsync([id], ct);

    public async Task<bool> LockBranch(long id, CancellationToken ct)
    {
        var rows = await db.Branches.FromSqlInterpolated($"SELECT * FROM branches WHERE id = {id} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct);
        return rows.Count != 0;
    }

    public async Task<long> NextPitchNumber(long branchId, CancellationToken ct)
    {
        var last = await db.Pitches.Where(x => x.BranchId == branchId)
            .MaxAsync(x => (long?)x.PitchNumber, ct);
        return (last ?? 0) + 1;
    }

    public Task<int> AddNotification(long userId, long bookingId, string eventId, string message, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications (userId, bookingId, eventId, message, createdAt)
            VALUES ({userId}, {bookingId}, {eventId}, {message}, {DateTime.UtcNow})
            ON DUPLICATE KEY UPDATE id = id
            """, ct);

    public async Task<Booking?> Lock(long id, CancellationToken ct)
    {
        var rows = await db.Bookings.FromSqlInterpolated($"SELECT * FROM bookings WHERE id = {id} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    // The caller must hold the booking row lock inside a transaction.
    public async Task<bool> ExpireHold(Booking? booking, DateTime now, CancellationToken ct)
    {
        if (booking is not { Status: "PendingPayment", ExpiresAtUtc: { } expires } || expires > now)
            return false;
        var payment = await db.Payments.SingleAsync(x => x.BookingId == booking.Id, ct);
        booking.Status = "Expired";
        payment.Status = "Expired";
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<long> BranchOf(long pitchId, CancellationToken ct) =>
        db.Pitches.Where(x => x.Id == pitchId).Select(x => x.BranchId).SingleOrDefaultAsync(ct);

    public Task<bool> Manages(long userId, long branchId, CancellationToken ct) =>
        db.Managers.AnyAsync(x => x.ManagerId == userId && x.BranchId == branchId, ct);
}
