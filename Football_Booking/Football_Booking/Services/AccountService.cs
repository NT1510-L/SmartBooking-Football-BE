using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Football_Booking.Repositories;

namespace Football_Booking.Services;

public sealed record Credentials(string Email, string Password, string? FullName);

public sealed class AccountService(BookingRepository repo, IPasswordHasher<Account> hasher, IConfiguration config)
{
    public Task<IResult> Register(Credentials input, CancellationToken ct) =>
        CreateAccount(input, "User", ct);

    public async Task<IResult> Login(Credentials input, CancellationToken ct)
    {
        var account = await repo.Users.SingleOrDefaultAsync(x => x.Email == input.Email && x.IsActive, ct);
        if (account is null || string.IsNullOrEmpty(input.Password) ||
            hasher.VerifyHashedPassword(account, account.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            return Results.Unauthorized();
        var claims = new[] { new Claim("sub", account.Id.ToString()), new Claim("role", account.Role) };
        var signing = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("football-booking", "football-web", claims,
            expires: DateTime.UtcNow.AddHours(2), signingCredentials: signing);
        return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), account.Id, account.FullName, account.Role });
    }

    public Task<IResult> CreateManager(Credentials input, CancellationToken ct) =>
        CreateAccount(input, "Manager", ct);

    public Task<Account?> FindByEmail(string email, CancellationToken ct) =>
        repo.Users.SingleOrDefaultAsync(x => x.Email == email, ct);

    public async Task<IResult> CreateAccount(Credentials input, string role, CancellationToken ct)
    {
        if (!ValidText(input.Email, 254) || !System.Net.Mail.MailAddress.TryCreate(input.Email, out var email) ||
            email.Address != input.Email || input.Password is not { Length: >= 8 and <= 128 } || !ValidText(input.FullName, 150))
            return Results.BadRequest(new { message = "Email, họ tên hoặc mật khẩu không hợp lệ (mật khẩu 8–128 ký tự)." });
        var account = new Account { Email = email.Address.ToLowerInvariant(), FullName = input.FullName!, Role = role };
        account.PasswordHash = hasher.HashPassword(account, input.Password);
        repo.Add(account);
        await repo.SaveChangesAsync(ct);
        return Results.Created($"/api/accounts/{account.Id}", new { account.Id, account.Email, account.FullName, account.Role });
    }

    private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
}
