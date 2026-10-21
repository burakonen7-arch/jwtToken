using System.ComponentModel.DataAnnotations;
namespace JwtToken.Application;
public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(12), MaxLength(128)] string Password);
public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password);
public sealed record RefreshRequest([Required, MaxLength(256)] string RefreshToken);
public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, string TokenType = "Bearer");
public sealed record UserResponse(Guid Id, string Email);
public interface IUserService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct);
    Task LogoutAsync(RefreshRequest request, CancellationToken ct);
    Task<UserResponse> GetAsync(Guid id, CancellationToken ct);
}
public sealed class AuthenticationFailedException : Exception;
public sealed class EmailInUseException : Exception;
