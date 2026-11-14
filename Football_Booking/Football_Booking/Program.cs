using System.Text;
using System.Net;
using Football_Booking.Api;
using Football_Booking.Repositories;
using Football_Booking.Services;
using Grpc.Core;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartBooking.Pricing;

var builder = WebApplication.CreateBuilder(args);
// This file is intentionally git-ignored. It lets Visual Studio run the API
// against a developer's local MySQL without committing credentials.
builder.Configuration.AddJsonFile("appsettings.Development.Local.json", optional: true, reloadOnChange: true);
var config = builder.Configuration;
var connection = ResolveMySqlConnection(config, "Booking", "Database:BookingPassword");
var connectionOptions = new MySqlConnector.MySqlConnectionStringBuilder(connection)
{
    GuidFormat = MySqlConnector.MySqlGuidFormat.None,
    DateTimeKind = MySqlConnector.MySqlDateTimeKind.Utc
};
builder.Services.AddDbContext<BookingDb>(options =>
    options.UseMySql(connectionOptions.ConnectionString, new MySqlServerVersion(new Version(8, 0, 46)),
        mysql => mysql.EnableRetryOnFailure()));
builder.Services.AddScoped<BookingRepository>();
if (config["RunMode"] == "Worker")
{
    // The worker has no HTTP endpoints. Bind an ephemeral loopback port so it
    // can run beside the API locally without colliding with the API launch profile.
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
    builder.Services.AddHostedService<BookingWorker>();
    await builder.Build().RunAsync();
    return;
}

var key = config["Jwt:Key"];
if (key is null || Encoding.UTF8.GetByteCount(key) < 32)
    throw new InvalidOperationException("Set Jwt__Key to at least 32 bytes.");
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<BookingQueryService>();
builder.Services.AddScoped<ManagementService>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddGrpcClient<PricingService.PricingServiceClient>(o =>
    o.Address = new Uri(config["Pricing:Address"]
        ?? (config["Pricing:HostPort"] is { Length: > 0 } hostPort ? $"http://{hostPort}" : "http://localhost:5081")))
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = "football-booking", ValidAudience = "football-web", RoleClaimType = "role",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireRole("Admin"))
    .AddPolicy("Staff", p => p.RequireRole("Admin", "Manager"))
    .AddPolicy("Customer", p => p.RequireRole("User"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(config["Web:Origin"] ?? "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.OperationFilter<BearerOperationFilter>();
});

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, message) = error switch
    {
        RpcException { StatusCode: StatusCode.NotFound } rpc => (404, rpc.Status.Detail),
        RpcException { StatusCode: StatusCode.InvalidArgument } rpc => (400, rpc.Status.Detail),
        RpcException { StatusCode: StatusCode.AlreadyExists } rpc => (409, rpc.Status.Detail),
        RpcException => (503, "Dịch vụ bảng giá chưa sẵn sàng."),
        DbUpdateException { InnerException: MySqlConnector.MySqlException { Number: 1062 } } =>
            (409, "Dữ liệu bị trùng hoặc khung giờ đã được đặt."),
        _ => (500, "Không thể hoàn tất yêu cầu.")
    };
    await Results.Problem(message, statusCode: status).ExecuteAsync(context);
}));
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapBookingApi();

var email = config["BootstrapAdmin:Email"];
var password = config["BootstrapAdmin:Password"];
if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
{
    await using var scope = app.Services.CreateAsyncScope();
    var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
    var existing = await accounts.FindByEmail(email, CancellationToken.None);
    if (existing is null)
    {
        var result = await accounts.CreateAccount(new Credentials(email, password, "Admin"), "Admin", CancellationToken.None);
        if (result is not IStatusCodeHttpResult { StatusCode: 201 })
            throw new InvalidOperationException("Bootstrap admin credentials are invalid.");
    }
    else if (existing.Role != "Admin")
        throw new InvalidOperationException("Bootstrap email is already used by another role.");
}
await app.RunAsync();

static string ResolveMySqlConnection(IConfiguration config, string connectionName, string passwordKey)
{
    if (config.GetConnectionString(connectionName) is { Length: > 0 } configured)
        return configured;

    var host = config["Database:Host"];
    var password = config[passwordKey];
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(password))
        throw new InvalidOperationException(
            "Set ConnectionStrings__Booking, or Database__Host and Database__BookingPassword.");

    return new MySqlConnector.MySqlConnectionStringBuilder
    {
        Server = host,
        Port = uint.TryParse(config["Database:Port"], out var port) ? port : 3306,
        Database = "booking_db",
        UserID = "booking_app",
        Password = password,
        GuidFormat = MySqlConnector.MySqlGuidFormat.None,
        DateTimeKind = MySqlConnector.MySqlDateTimeKind.Utc
    }.ConnectionString;
}
