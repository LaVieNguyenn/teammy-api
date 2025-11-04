using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Teammy.Application.Auth.Dtos;
using Teammy.Application.Auth.Queries;
using Teammy.Application.Auth.Services;

namespace Teammy.Api.Controllers;

/// <summary>
/// Auth endpoints: /login, /me
/// Controller chỉ điều phối, không truy cập EF/DbContext.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthenticationService _authentication;
    private readonly CurrentUserQueryService _currentUserQuery;

    public AuthController(AuthenticationService authentication, CurrentUserQueryService currentUserQuery)
    {
        _authentication = authentication;
        _currentUserQuery = currentUserQuery;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _authentication.LoginWithFirebaseAsync(request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized("Invalid token");

        var me = await _currentUserQuery.GetAsync(userId, ct);
        return me is null ? NotFound() : Ok(me);
    }
}
