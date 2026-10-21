using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using JwtToken.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace JwtToken.Api.Controllers;
[ApiController]
[Route("api/users")]
[Produces("application/json")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = await users.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Me), user);
    }
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct) => Ok(await users.LoginAsync(request, ct));
    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken ct) => Ok(await users.RefreshAsync(request, ct));
    [HttpPost("logout"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await users.LogoutAsync(request, ct);
        return NoContent();
    }
    [HttpGet("me"), Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)) return Unauthorized();
        return Ok(await users.GetAsync(id, ct));
    }
}
