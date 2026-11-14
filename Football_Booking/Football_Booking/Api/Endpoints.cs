using System.Security.Claims;
using Football_Booking.Services;

namespace Football_Booking.Api;

public static class Endpoints
{
    public static void MapBookingApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        var auth = api.MapGroup("/auth").WithTags("Authentication");
        auth.MapPost("/register", (AccountService service, Credentials input, CancellationToken ct) =>
            service.Register(input, ct));
        auth.MapPost("/login", (AccountService service, Credentials input, CancellationToken ct) =>
            service.Login(input, ct));

        var catalogue = api.MapGroup("").WithTags("Public - Catalogue");
        catalogue.MapGet("/branches", (BookingQueryService service, string? search, string? area, int? page, int? size, CancellationToken ct) =>
            service.ListBranches(search, area, page, size, ct));
        catalogue.MapGet("/branches/{id:long}/pitches", (BookingQueryService service, long id, CancellationToken ct) =>
            service.ListActivePitches(id, ct));
        catalogue.MapGet("/pitches/{id:long}/availability", (BookingQueryService service, long id, DateOnly date, CancellationToken ct) =>
            service.Availability(id, date, ct));
        catalogue.MapGet("/branches/{id:long}/quote", (BookingQueryService service, long id, DateOnly date, int hour, CancellationToken ct) =>
            service.Quote(id, date, hour, ct));

        var bookings = api.MapGroup("/bookings").RequireAuthorization("Customer").WithTags("User - Bookings");
        bookings.MapPost("", (BookingInput input, ClaimsPrincipal actor, BookingService bookings, CancellationToken ct) =>
            bookings.Create(Id(actor), input, ct)).RequireAuthorization("Customer");
        bookings.MapGet("", (BookingQueryService service, ClaimsPrincipal actor, long? branchId, string? status, DateOnly? date, string? sort, int? page, int? size, CancellationToken ct) =>
            service.ListBookings(actor, branchId, status, date, sort, page, size, ct));
        bookings.MapGet("/{id:long}", (BookingQueryService service, long id, ClaimsPrincipal actor, CancellationToken ct) =>
            service.GetBooking(id, actor, ct));
        bookings.MapPost("/{id:long}/cancel", (long id, ClaimsPrincipal actor, BookingService service, CancellationToken ct) =>
            service.Cancel(id, Id(actor), actor.FindFirstValue("role")!, ct));
        var notifications = api.MapGroup("/notifications").RequireAuthorization().WithTags("Authenticated - Notifications");
        notifications.MapGet("", (BookingQueryService service, ClaimsPrincipal actor, int? page, int? size, CancellationToken ct) =>
            service.ListNotifications(actor, page, size, ct));
        notifications.MapPost("/{id:long}/read", (BookingQueryService service, long id, ClaimsPrincipal actor, CancellationToken ct) =>
            service.ReadNotification(id, actor, ct));
        api.MapPost("/payments/sepay/webhook", (SepayEvent data, HttpRequest request, BookingService service, CancellationToken ct) =>
            service.Receive(data, request.Headers.Authorization.ToString(), ct))
            .WithTags("Payment - SePay webhook");
        MapManagement(api);
    }

    private static void MapManagement(RouteGroupBuilder api)
    {
        var adminBranches = api.MapGroup("/admin/branches").RequireAuthorization("Admin").WithTags("Admin - Branches");
        adminBranches.MapGet("", (ManagementService service, CancellationToken ct) =>
            service.ListBranches(ct));
        var adminPayments = api.MapGroup("/admin/payment-receipts").RequireAuthorization("Admin").WithTags("Admin - Payment reconciliation");
        adminPayments.MapGet("", (ManagementService service, int? page, int? size, CancellationToken ct) =>
            service.ListPaymentReceipts(page, size, ct));
        adminBranches.MapPost("", (ManagementService service, BranchInput input, CancellationToken ct) =>
            service.CreateBranch(input, ct));
        adminBranches.MapPut("/{id:long}", (ManagementService service, long id, BranchInput input, CancellationToken ct) =>
            service.UpdateBranch(id, input, ct));
        adminBranches.MapDelete("/{id:long}", (ManagementService service, long id, CancellationToken ct) =>
            service.DeactivateBranch(id, ct));
        var adminManagers = api.MapGroup("/admin/managers").RequireAuthorization("Admin").WithTags("Admin - Managers");
        adminManagers.MapGet("", (ManagementService service, CancellationToken ct) =>
            service.ListManagers(ct));
        adminManagers.MapPost("", (AccountService service, Credentials input, CancellationToken ct) =>
            service.CreateManager(input, ct));
        adminBranches.MapPut("/{id:long}/manager", (ManagementService service, long id, ManagerInput input, CancellationToken ct) =>
            service.AssignManager(id, input, ct));
        adminBranches.MapDelete("/{id:long}/manager", (ManagementService service, long id, CancellationToken ct) =>
            service.UnassignManager(id, ct));

        var ops = api.MapGroup("/management").RequireAuthorization("Staff");
        ops.MapGet("/bookings", (BookingQueryService service, ClaimsPrincipal actor, long? branchId, string? status, DateOnly? date, string? sort, int? page, int? size, CancellationToken ct) =>
            service.ListBookings(actor, branchId, status, date, sort, page, size, ct))
            .WithTags("Manager/Admin - Bookings");
        ops.MapGet("/bookings/{id:long}", (BookingQueryService service, long id, ClaimsPrincipal actor, CancellationToken ct) =>
            service.GetBooking(id, actor, ct))
            .WithTags("Manager/Admin - Bookings");
        var managerBranches = ops.MapGroup("/branches").WithTags("Manager/Admin - Branches");
        managerBranches.MapGet("", (ManagementService service, ClaimsPrincipal actor, CancellationToken ct) =>
            service.ListManagedBranches(actor, ct));
        var managerPitches = ops.MapGroup("").WithTags("Manager/Admin - Pitches");
        managerPitches.MapGet("/branches/{id:long}/pitches", (ManagementService service, long id, ClaimsPrincipal actor, CancellationToken ct) =>
            service.ListPitches(id, actor, ct));
        managerPitches.MapPost("/branches/{id:long}/pitches", (ManagementService service, long id, PitchInput input, ClaimsPrincipal actor, CancellationToken ct) =>
            service.CreatePitch(id, input, actor, ct));
        managerPitches.MapPut("/pitches/{id:long}", (ManagementService service, long id, PitchInput input, ClaimsPrincipal actor, CancellationToken ct) =>
            service.UpdatePitch(id, input, actor, ct));
        var prices = ops.MapGroup("/branches/{id:long}/prices").WithTags("Manager/Admin - Pricing");
        prices.MapGet("", (ManagementService service, long id, ClaimsPrincipal actor, CancellationToken ct) =>
            service.ListPrices(id, actor, ct));
        prices.MapPost("", (ManagementService service, long id, PriceInput input, ClaimsPrincipal actor, CancellationToken ct) =>
            service.CreatePrice(id, input, actor, ct));
        prices.MapPut("/{policyId:long}", (ManagementService service, long id, long policyId, PriceInput input, ClaimsPrincipal actor, CancellationToken ct) =>
            service.UpdatePrice(id, policyId, input, actor, ct));
        prices.MapDelete("/{policyId:long}", (ManagementService service, long id, long policyId, ClaimsPrincipal actor, CancellationToken ct) =>
            service.DeletePrice(id, policyId, actor, ct));
    }

    private static long Id(ClaimsPrincipal actor) => long.Parse(actor.FindFirstValue("sub")!);
}
