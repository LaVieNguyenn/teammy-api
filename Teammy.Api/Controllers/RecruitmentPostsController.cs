using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Posts.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/recruitment-posts")]
public sealed class RecruitmentPostsController(RecruitmentPostService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> Create([FromBody] CreateRecruitmentPostRequest req, CancellationToken ct)
    {
        try
        {
            var id = await service.CreateAsync(GetUserId(), req, ct);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<RecruitmentPostSummaryDto>> List([FromQuery] string? skills, [FromQuery] Guid? majorId, [FromQuery] string? status, CancellationToken ct)
        => service.ListAsync(skills, majorId, status, ct);

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<RecruitmentPostDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var d = await service.GetAsync(id, ct);
        return d is null ? NotFound() : Ok(d);
    }

    [HttpPost("{id:guid}/applications")]
    [Authorize]
    public async Task<ActionResult> Apply([FromRoute] Guid id, [FromBody] CreateApplicationRequest req, CancellationToken ct)
    {
        try
        {
            await service.ApplyAsync(id, GetUserId(), req?.Message, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("{id:guid}/applications")]
    [Authorize]
    public Task<IReadOnlyList<ApplicationDto>> ListApplications([FromRoute] Guid id, CancellationToken ct)
        => service.ListApplicationsAsync(id, GetUserId(), ct);

    [HttpPost("{id:guid}/applications/{appId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult> Accept([FromRoute] Guid id, [FromRoute] Guid appId, CancellationToken ct)
    {
        try
        {
            await service.AcceptAsync(id, appId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/applications/{appId:guid}/reject")]
    [Authorize]
    public async Task<ActionResult> Reject([FromRoute] Guid id, [FromRoute] Guid appId, CancellationToken ct)
    {
        try
        {
            await service.RejectAsync(id, appId, GetUserId(), ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<ActionResult> Update([FromRoute] Guid id, [FromBody] UpdateRecruitmentPostRequest req, CancellationToken ct)
    {
        try
        {
            await service.UpdateAsync(id, GetUserId(), req, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

