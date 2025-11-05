using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Posts.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/profile-posts")]
public sealed class ProfilePostsController(ProfilePostService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> Create([FromBody] CreateProfilePostRequest req, CancellationToken ct)
    {
        try
        {
            var id = await service.CreateAsync(GetUserId(), req, ct);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<ProfilePostSummaryDto>> List([FromQuery] string? skills, [FromQuery] Guid? majorId, [FromQuery] string? status, CancellationToken ct)
        => service.ListAsync(skills, majorId, status, ct);

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProfilePostDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var d = await service.GetAsync(id, ct);
        return d is null ? NotFound() : Ok(d);
    }

    [HttpPost("{id:guid}/invites")]
    [Authorize]
    public async Task<ActionResult> Invite([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await service.InviteAsync(id, GetUserId(), ct);
            return Accepted();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

