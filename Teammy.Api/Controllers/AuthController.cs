using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Api.Contracts;
using Teammy.Application.Auth;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<AuthController> _log;

    public AuthController(IAuthService auth, ILogger<AuthController> log)
    {
        _auth = auth;
        _log = log;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            return BadRequest(new { error = "idToken required" });
        try
        {
            var r = await _auth.LoginWithFirebaseAsync(request.IdToken, ct);

            return Ok(new AuthResponse
            {
                AccessToken = r.AccessToken,
                User = new UserDto
                {
                    Id = r.UserId,
                    Email = r.Email,
                    Name = r.Name,
                    PhotoUrl = r.PhotoUrl,
                    Role = r.Role
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "USER_NOT_IMPORTED", message = "User is not provisioned. Contact administrator." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "USER_INACTIVE")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "USER_INACTIVE", message = "Account is disabled." });
        }
    }


    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var uid = User.FindFirstValue("uid");
        if (!Guid.TryParse(uid, out var id))
            return Unauthorized();
        try
        {
            var me = await _auth.GetMeAsync(id, ct);
            return Ok(new
            {
                id = me.Id,
                email = me.Email,
                name = me.Name,
                role = me.Role,
                photoUrl = me.PhotoUrl
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "USER_NOT_FOUND" });
        }
    }
}
