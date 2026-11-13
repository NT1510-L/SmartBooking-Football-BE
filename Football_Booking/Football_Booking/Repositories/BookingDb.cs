using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Football_Booking.Repositories;

public sealed class BookingDb(DbContextOptions<BookingDb> options) : DbContext(options)
{
    public DbSet<Account> Users => Set<Account>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Pitch> Pitches => Set<Pitch>();
    public DbSet<Assignment> Managers => Set<Assignment>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Outbox> Outbox => Set<Outbox>();
    public DbSet<Notice> Notifications => Set<Notice>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Assignment>().HasKey(x => x.ManagerId);
        builder.Entity<Receipt>().HasKey(x => x.SepayId);
        builder.Entity<Receipt>().Property(x => x.SepayId).ValueGeneratedNever();
        builder.Entity<Outbox>().HasKey(x => x.EventId);
        builder.Entity<Booking>().Property(x => x.EndAtUtc).ValueGeneratedOnAddOrUpdate();
        foreach (var entity in builder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(char.ToLowerInvariant(property.Name[0]) + property.Name[1..]);

        // Database uses lower camel case: pitchId, createdAt, startTime.
        // UTC is an application storage convention, not a physical column name.
        builder.Entity<Booking>().Property(x => x.StartAtUtc).HasColumnName("startTime");
        builder.Entity<Booking>().Property(x => x.EndAtUtc).HasColumnName("endTime");
        builder.Entity<Booking>().Property(x => x.CreatedAtUtc).HasColumnName("createdAt");
        builder.Entity<Booking>().Property(x => x.ExpiresAtUtc).HasColumnName("expiresAt");
        builder.Entity<Booking>().Property(x => x.CancelledAtUtc).HasColumnName("cancelledAt");
        builder.Entity<Payment>().Property(x => x.PaidAtUtc).HasColumnName("paidAt");
        builder.Entity<Receipt>().Property(x => x.ReceivedAtUtc).HasColumnName("receivedAt");
        builder.Entity<Outbox>().Property(x => x.OccurredAtUtc).HasColumnName("occurredAt");
        builder.Entity<Outbox>().Property(x => x.PublishedAtUtc).HasColumnName("publishedAt");
        builder.Entity<Notice>().Property(x => x.CreatedAtUtc).HasColumnName("createdAt");
        builder.Entity<Notice>().Property(x => x.ReadAtUtc).HasColumnName("readAt");
    }
}

[Table("users")]
public sealed class Account
{
    public long Id { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
}

[Table("branches")]
public sealed class Branch
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string Area { get; set; }
    public byte OpeningHour { get; set; }
    public byte ClosingHour { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("pitches")]
public sealed class Pitch
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PitchNumber { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("branchManagers")]
public sealed class Assignment
{
    public long ManagerId { get; set; }
    public long BranchId { get; set; }
}

[Table("bookings")]
public sealed class Booking
{
    public long Id { get; set; }
    public long PitchId { get; set; }
    public long UserId { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public long? CancelledByUserId { get; set; }
    public string Status { get; set; } = "PendingPayment";
    public long PriceVnd { get; set; }
    public long PricingPolicyId { get; set; }
}

[Table("payments")]
public sealed class Payment
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public required string Code { get; set; }
    public long AmountVnd { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? PaidAtUtc { get; set; }
}

[Table("paymentReceipts")]
public sealed class Receipt
{
    public long SepayId { get; set; }
    public long PaymentId { get; set; }
    public long AmountVnd { get; set; }
    public required string Outcome { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}

[Table("outboxMessages")]
public sealed class Outbox
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public long BookingId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}

[Table("notifications")]
public sealed class Notice
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long BookingId { get; set; }
    public required string EventId { get; set; }
    public required string Message { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}
