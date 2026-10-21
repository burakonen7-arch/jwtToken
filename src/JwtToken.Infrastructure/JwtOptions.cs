using System.ComponentModel.DataAnnotations;
namespace JwtToken.Infrastructure;
public sealed class JwtOptions
{
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Required, MinLength(64)] public string SigningKey { get; set; } = "";
    [Range(1, 30)] public int AccessTokenMinutes { get; set; } = 10;
    [Range(1, 30)] public int RefreshTokenDays { get; set; } = 7;
}
