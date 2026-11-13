using System.Security.Claims;
using SmartBooking.Pricing;
using Microsoft.EntityFrameworkCore;
using Football_Booking.Repositories;

namespace Football_Booking.Services;

public sealed record BranchInput(string Name, string Address, string Area, byte OpeningHour, byte ClosingHour, bool IsActive = true);
public sealed record PitchInput(string Name, bool IsActive);
public sealed record ManagerInput(long ManagerId);
public sealed record PriceInput(string EffectiveFrom, long RegularPriceVnd, long PeakPriceVnd, int PeakStartHour, int PeakEndHour);

public sealed class ManagementService(BookingRepository repo, PricingService.PricingServiceClient pricing)
{
    public async Task<IResult> ListBranches(CancellationToken ct) =>
        Results.Ok(await repo.Branches.OrderBy(x => x.Id).ToListAsync(ct));

    public async Task<IResult> ListPaymentReceipts(int? page, int? size, CancellationToken ct) =>
        Results.Ok(await (from receipt in repo.Receipts.AsNoTracking()
            join payment in repo.Payments on receipt.PaymentId equals payment.Id
            where receipt.Outcome != "Accepted"
            orderby receipt.ReceivedAtUtc descending, receipt.SepayId descending
            select new { receipt.SepayId, payment.BookingId, payment.Code, receipt.AmountVnd,
                ExpectedAmountVnd = payment.AmountVnd, receipt.Outcome, receipt.ReceivedAtUtc })
            .Skip(Offset(page, size)).Take(PageSize(size)).ToListAsync(ct));

    public async Task<IResult> CreateBranch(BranchInput input, CancellationToken ct)
    {
        if (!Valid(input)) return Results.BadRequest();
        var branch = new Branch { Name = input.Name, Address = input.Address, Area = input.Area };
        Apply(branch, input);
        repo.Add(branch);
        await repo.SaveChangesAsync(ct);
        return Results.Created($"/api/admin/branches/{branch.Id}", branch);
    }

    public async Task<IResult> UpdateBranch(long id, BranchInput input, CancellationToken ct)
    {
        if (!Valid(input)) return Results.BadRequest();
        var branch = await repo.FindBranch(id, ct);
        if (branch is null) return Results.NotFound();
        Apply(branch, input);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(branch);
    }

    public async Task<IResult> DeactivateBranch(long id, CancellationToken ct)
    {
        var count = await repo.Branches.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
        return count == 0 ? Results.NotFound() : Results.NoContent();
    }

    public async Task<IResult> ListManagers(CancellationToken ct) =>
        Results.Ok(await (from account in repo.Users where account.Role == "Manager"
            join assignment in repo.Managers on account.Id equals assignment.ManagerId into assigned
            from assignment in assigned.DefaultIfEmpty()
            select new { account.Id, account.Email, account.FullName, BranchId = (long?)assignment.BranchId }).ToListAsync(ct));

    public async Task<IResult> AssignManager(long id, ManagerInput input, CancellationToken ct)
    {
        if (!await repo.Branches.AnyAsync(x => x.Id == id, ct) ||
            !await repo.Users.AnyAsync(x => x.Id == input.ManagerId && x.Role == "Manager" && x.IsActive, ct))
            return Results.NotFound();
        await using var tx = await repo.BeginTransactionAsync(ct);
        await repo.Managers.Where(x => x.BranchId == id || x.ManagerId == input.ManagerId).ExecuteDeleteAsync(ct);
        repo.Add(new Assignment { BranchId = id, ManagerId = input.ManagerId });
        await repo.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> UnassignManager(long id, CancellationToken ct)
    {
        await repo.Managers.Where(x => x.BranchId == id).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> ListManagedBranches(ClaimsPrincipal actor, CancellationToken ct) =>
        Results.Ok(await repo.Branches.Where(x => actor.IsInRole("Admin") ||
            repo.Managers.Any(m => m.BranchId == x.Id && m.ManagerId == Id(actor))).ToListAsync(ct));

    public async Task<IResult> ListPitches(long id, ClaimsPrincipal actor, CancellationToken ct) =>
        await Allowed(actor, id, ct)
            ? Results.Ok(await repo.Pitches.Where(x => x.BranchId == id).OrderBy(x => x.PitchNumber).ToListAsync(ct))
            : Results.Forbid();

    public async Task<IResult> CreatePitch(long branchId, PitchInput input, ClaimsPrincipal actor, CancellationToken ct)
    {
        var name = input.Name?.Trim();
        if (name is null || !ValidText(name, 120))
            return Results.BadRequest(new { message = "Tên sân phải có từ 1 đến 120 ký tự." });

        await using var tx = await repo.BeginTransactionAsync(ct);
        if (!await repo.LockBranch(branchId, ct)) return Results.NotFound();
        if (!await Allowed(actor, branchId, ct)) return Results.Forbid();

        var pitch = new Pitch
        {
            BranchId = branchId,
            PitchNumber = await repo.NextPitchNumber(branchId, ct),
            Name = name,
            IsActive = input.IsActive
        };
        repo.Add(pitch);
        await repo.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/api/management/branches/{branchId}/pitches", pitch);
    }

    public async Task<IResult> UpdatePitch(long id, PitchInput input, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!ValidText(input.Name, 120)) return Results.BadRequest();
        var pitch = await repo.FindPitch(id, ct);
        if (pitch is null) return Results.NotFound();
        if (!await Allowed(actor, pitch.BranchId, ct)) return Results.Forbid();
        pitch.Name = input.Name;
        pitch.IsActive = input.IsActive;
        await repo.SaveChangesAsync(ct);
        return Results.Ok(pitch);
    }

    public async Task<IResult> ListPrices(long id, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!await Allowed(actor, id, ct)) return Results.Forbid();
        var reply = await pricing.ListAsync(new BranchRequest { BranchId = id }, deadline: Deadline(), cancellationToken: ct);
        return Results.Ok(reply.Policies.ToArray());
    }

    public Task<IResult> CreatePrice(long id, PriceInput input, ClaimsPrincipal actor, CancellationToken ct) =>
        SavePrice(id, 0, input, actor, ct);

    public Task<IResult> UpdatePrice(long id, long policyId, PriceInput input, ClaimsPrincipal actor, CancellationToken ct) =>
        SavePrice(id, policyId, input, actor, ct);

    public async Task<IResult> DeletePrice(long id, long policyId, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!await Allowed(actor, id, ct)) return Results.Forbid();
        await pricing.RemoveAsync(new PolicyKey { BranchId = id, Id = policyId }, deadline: Deadline(), cancellationToken: ct);
        return Results.NoContent();
    }

    private async Task<IResult> SavePrice(long id, long policyId, PriceInput input, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!await Allowed(actor, id, ct)) return Results.Forbid();
        if (!await repo.Branches.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
        return Results.Ok(await pricing.SaveAsync(new Policy
        {
            Id = policyId, BranchId = id, EffectiveFrom = input.EffectiveFrom ?? "", RegularPriceVnd = input.RegularPriceVnd,
            PeakPriceVnd = input.PeakPriceVnd, PeakStartHour = input.PeakStartHour, PeakEndHour = input.PeakEndHour
        }, deadline: Deadline(), cancellationToken: ct));
    }

    private static void Apply(Branch branch, BranchInput input)
    {
        branch.Name = input.Name;
        branch.Address = input.Address;
        branch.Area = input.Area;
        branch.OpeningHour = input.OpeningHour;
        branch.ClosingHour = input.ClosingHour;
        branch.IsActive = input.IsActive;
    }

    private static bool Valid(BranchInput input) => ValidText(input.Name, 150) && ValidText(input.Address, 255) &&
        ValidText(input.Area, 120) && input.OpeningHour < input.ClosingHour && input.ClosingHour <= 24;
    private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private static long Id(ClaimsPrincipal actor) => long.Parse(actor.FindFirstValue("sub")!);
    private async Task<bool> Allowed(ClaimsPrincipal actor, long branchId, CancellationToken ct) =>
        actor.IsInRole("Admin") || await repo.Manages(Id(actor), branchId, ct);
    private static int PageSize(int? size) => Math.Clamp(size ?? 20, 1, 100);
    private static int Offset(int? page, int? size) => (Math.Clamp(page ?? 1, 1, 10000) - 1) * PageSize(size);
    private static DateTime Deadline() => DateTime.UtcNow.AddSeconds(5);
}
