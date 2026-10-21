using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JwtToken.Application;
using JwtToken.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
namespace JwtToken.Infrastructure;
public sealed class UserService(AppDbContext db, IPasswordHasher<User> hasher, IOptions<JwtOptions> options) : IUserService
{
    private readonly JwtOptions jwt = options.Value;
    // Equal-cost password verification when an email is unknown.
    private static readonly User Dummy = new();
    private static readonly string DummyHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 })).HashPassword(Dummy, "dummy-password-for-timing-only");
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = new User { Email = Normalize(request.Email) };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new EmailInUseException(); }
        return new(user.Id, user.Email);
    }
    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == Normalize(request.Email), ct);
        var result = hasher.VerifyHashedPassword(user ?? Dummy, user?.PasswordHash ?? DummyHash, request.Password);
        if (user is null || result == PasswordVerificationResult.Failed) throw new AuthenticationFailedException();
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        var response = Issue(user, Guid.NewGuid());
        await db.SaveChangesAsync(ct);
        return response;
    }
    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        // Serialize rotation per family so concurrent requests cannot create two active descendants.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(request.RefreshToken);
        var token = await db.RefreshTokens.Include(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null) throw new AuthenticationFailedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({token.FamilyId.ToString()}, 0))", ct);
        await db.Entry(token).ReloadAsync(ct);
        if (token.RevokedAt is not null)
        {
            await RevokeFamily(token.FamilyId, ct);
            await tx.CommitAsync(ct);
            throw new AuthenticationFailedException();
        }
        if (token.ExpiresAt <= DateTimeOffset.UtcNow) throw new AuthenticationFailedException();
        token.RevokedAt = DateTimeOffset.UtcNow;
        var response = Issue(token.User, token.FamilyId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }
    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(request.RefreshToken);
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({token.FamilyId.ToString()}, 0))", ct);
            await RevokeFamily(token.FamilyId, ct);
        }
        await tx.CommitAsync(ct);
    }
    private Task<int> RevokeFamily(Guid familyId, CancellationToken ct) => db.RefreshTokens
        .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTimeOffset?)DateTimeOffset.UtcNow), ct);
    public async Task<UserResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AuthenticationFailedException();
        return new(user.Id, user.Email);
    }
    private TokenResponse Issue(User user, Guid familyId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);
        var access = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
             new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
            now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), SecurityAlgorithms.HmacSha256));
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshExpires = now.AddDays(jwt.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = Hash(refresh), FamilyId = familyId, ExpiresAt = refreshExpires });
        return new(new JwtSecurityTokenHandler().WriteToken(access), expires, refresh, refreshExpires);
    }
}
