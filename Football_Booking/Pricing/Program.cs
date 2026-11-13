using System.Globalization;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using SmartBooking.Pricing;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Development.Local.json", optional: true, reloadOnChange: true);
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(
    builder.Configuration.GetValue("PricingPort", 5081), endpoint => endpoint.Protocols = HttpProtocols.Http2));
var connection = ResolveMySqlConnection(builder.Configuration);
builder.Services.AddDbContext<PricingDb>(options => options.UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 46))));
builder.Services.AddGrpc();
var app = builder.Build();
app.MapGrpcService<PricingEndpoint>();
app.Run();

static string ResolveMySqlConnection(IConfiguration config)
{
    if (config.GetConnectionString("Pricing") is { Length: > 0 } configured)
        return configured;

    var host = config["Database:Host"];
    var password = config["Database:PricingPassword"];
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(password))
        throw new InvalidOperationException(
            "Set ConnectionStrings__Pricing, or Database__Host and Database__PricingPassword.");

    return new MySqlConnector.MySqlConnectionStringBuilder
    {
        Server = host,
        Port = uint.TryParse(config["Database:Port"], out var port) ? port : 3306,
        Database = "pricing_db",
        UserID = "pricing_app",
        Password = password,
        DateTimeKind = MySqlConnector.MySqlDateTimeKind.Utc
    }.ConnectionString;
}

sealed class PricingDb(DbContextOptions<PricingDb> options) : DbContext(options)
{
    public DbSet<PricePolicy> Policies => Set<PricePolicy>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var policy = builder.Entity<PricePolicy>().ToTable("pricingPolicies");
        policy.Property(x => x.Id).HasColumnName("id");
        policy.Property(x => x.BranchId).HasColumnName("branchId");
        policy.Property(x => x.EffectiveFrom).HasColumnName("effectiveFrom");
        policy.Property(x => x.RegularPriceVnd).HasColumnName("regularPriceVnd");
        policy.Property(x => x.PeakPriceVnd).HasColumnName("peakPriceVnd");
        policy.Property(x => x.PeakStartHour).HasColumnName("peakStartHour");
        policy.Property(x => x.PeakEndHour).HasColumnName("peakEndHour");
    }
}

sealed class PricePolicy
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public long RegularPriceVnd { get; set; }
    public long PeakPriceVnd { get; set; }
    public byte PeakStartHour { get; set; }
    public byte PeakEndHour { get; set; }

    public Policy ToContract() => new()
    {
        Id = Id, BranchId = BranchId, EffectiveFrom = EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        RegularPriceVnd = RegularPriceVnd, PeakPriceVnd = PeakPriceVnd,
        PeakStartHour = PeakStartHour, PeakEndHour = PeakEndHour
    };
}

sealed class PricingEndpoint(PricingDb db) : PricingService.PricingServiceBase
{
    public override async Task<PriceQuote> Quote(QuoteRequest request, ServerCallContext context)
    {
        var date = ParseDate(request.LocalDate);
        if (request.Hour is < 0 or > 23)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Giờ bắt đầu không hợp lệ."));
        var policy = await db.Policies.AsNoTracking()
            .Where(p => p.BranchId == request.BranchId && p.EffectiveFrom <= date)
            .OrderByDescending(p => p.EffectiveFrom).FirstOrDefaultAsync(context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Chi nhánh chưa có bảng giá hiệu lực."));
        return new PriceQuote
        {
            PolicyId = policy.Id,
            Amount = request.Hour >= policy.PeakStartHour && request.Hour < policy.PeakEndHour
                ? policy.PeakPriceVnd : policy.RegularPriceVnd
        };
    }

    public override async Task<PolicyList> List(BranchRequest request, ServerCallContext context)
    {
        var rows = await db.Policies.AsNoTracking().Where(p => p.BranchId == request.BranchId)
            .OrderByDescending(p => p.EffectiveFrom).ToListAsync(context.CancellationToken);
        var result = new PolicyList();
        result.Policies.AddRange(rows.Select(p => p.ToContract()));
        return result;
    }

    public override async Task<Policy> Save(Policy request, ServerCallContext context)
    {
        var date = ParseDate(request.EffectiveFrom);
        if (request.RegularPriceVnd <= 0 || request.PeakPriceVnd < request.RegularPriceVnd ||
            request.PeakStartHour < 0 || request.PeakEndHour > 24 || request.PeakStartHour >= request.PeakEndHour)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Bảng giá hoặc khung giờ cao điểm không hợp lệ."));
        var row = request.Id == 0 ? new PricePolicy { BranchId = request.BranchId } :
            await db.Policies.SingleOrDefaultAsync(p => p.Id == request.Id && p.BranchId == request.BranchId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Không tìm thấy bảng giá."));
        row.EffectiveFrom = date;
        row.RegularPriceVnd = request.RegularPriceVnd;
        row.PeakPriceVnd = request.PeakPriceVnd;
        row.PeakStartHour = (byte)request.PeakStartHour;
        row.PeakEndHour = (byte)request.PeakEndHour;
        if (request.Id == 0) db.Policies.Add(row);
        try { await db.SaveChangesAsync(context.CancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Đã có bảng giá cho ngày hiệu lực này."));
        }
        return row.ToContract();
    }

    public override async Task<PolicyKey> Remove(PolicyKey request, ServerCallContext context)
    {
        var count = await db.Policies.Where(p => p.Id == request.Id && p.BranchId == request.BranchId)
            .ExecuteDeleteAsync(context.CancellationToken);
        if (count == 0) throw new RpcException(new Status(StatusCode.NotFound, "Không tìm thấy bảng giá."));
        return request;
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year >= 1000
            ? date : throw new RpcException(new Status(StatusCode.InvalidArgument, "Ngày không hợp lệ."));
}
