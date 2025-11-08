using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Posts.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/recruitment-posts")]
public sealed class RecruitmentPostsController(RecruitmentPostService service, IConfiguration cfg) : ControllerBase
{
    private readonly bool _objectOnlyDefault = string.Equals(cfg["Api:Posts:DefaultShape"], "object", StringComparison.OrdinalIgnoreCase);
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
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> List([FromQuery] string? skills, [FromQuery] Guid? majorId, [FromQuery] string? status, [FromQuery] string? expand, [FromQuery] string? shape, CancellationToken ct)
    {
        var exp = ParseExpand(expand);
        var objectOnly = _objectOnlyDefault;
        if (!string.IsNullOrWhiteSpace(shape))
        {
            objectOnly = string.Equals(shape, "object", StringComparison.OrdinalIgnoreCase);
        }
        if (objectOnly)
        {
            exp |= ExpandOptions.Semester | ExpandOptions.Group | ExpandOptions.Major;
        }
        var items = await service.ListAsync(skills, majorId, status, exp, ct);
        if (!objectOnly) return Ok(items);

        var shaped = items.Select(d => new
        {
            id = d.Id,
            type = d.Type,
            status = d.Status,
            title = d.Title,
            description = d.Description,
            positionNeeded = d.PositionNeeded,
            createdAt = d.CreatedAt,
            applicationDeadline = d.ApplicationDeadline,
            currentMembers = d.CurrentMembers,
            semester = d.Semester,
            group = d.Group,
            major = d.Major
        });
        return Ok(shaped);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<RecruitmentPostDetailDto>> GetById([FromRoute] Guid id, [FromQuery] string? expand, [FromQuery] string? shape, CancellationToken ct)
    {
        var exp = ParseExpand(expand);
        var objectOnly = _objectOnlyDefault;
        if (!string.IsNullOrWhiteSpace(shape))
        {
            objectOnly = string.Equals(shape, "object", StringComparison.OrdinalIgnoreCase);
        }
        if (objectOnly)
        {
            exp |= ExpandOptions.Semester | ExpandOptions.Group | ExpandOptions.Major;
        }
        var d = await service.GetAsync(id, exp, ct);
        if (d is null) return NotFound();
        if (!objectOnly) return Ok(d);
        return Ok(new
        {
            id = d.Id,
            type = d.Type,
            status = d.Status,
            title = d.Title,
            description = d.Description,
            positionNeeded = d.PositionNeeded,
            createdAt = d.CreatedAt,
            applicationDeadline = d.ApplicationDeadline,
            currentMembers = d.CurrentMembers,
            semester = d.Semester,
            group = d.Group,
            major = d.Major
        });
    }

    private static Teammy.Application.Posts.Dtos.ExpandOptions ParseExpand(string? e)
    {
        var opt = Teammy.Application.Posts.Dtos.ExpandOptions.None;
        if (string.IsNullOrWhiteSpace(e)) return opt;
        foreach (var part in e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "semester": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.Semester; break;
                case "group": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.Group; break;
                case "major": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.Major; break;
            }
        }
        return opt;
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
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
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
