using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Posts.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/profile-posts")]
public sealed class ProfilePostsController(ProfilePostService service, IConfiguration cfg) : ControllerBase
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
            exp |= ExpandOptions.Semester | ExpandOptions.Major | ExpandOptions.User;
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
            skills = d.Skills,
            createdAt = d.CreatedAt,
            semester = d.Semester,
            user = d.User,
            major = d.Major
        });
        return Ok(shaped);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProfilePostDetailDto>> GetById([FromRoute] Guid id, [FromQuery] string? expand, [FromQuery] string? shape, CancellationToken ct)
    {
        var exp = ParseExpand(expand);
        var objectOnly = _objectOnlyDefault;
        if (!string.IsNullOrWhiteSpace(shape))
        {
            objectOnly = string.Equals(shape, "object", StringComparison.OrdinalIgnoreCase);
        }
        if (objectOnly)
        {
            exp |= ExpandOptions.Semester | ExpandOptions.Major | ExpandOptions.User;
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
            skills = d.Skills,
            createdAt = d.CreatedAt,
            semester = d.Semester,
            user = d.User,
            major = d.Major
        });
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
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
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
                case "major": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.Major; break;
                case "user": opt |= Teammy.Application.Posts.Dtos.ExpandOptions.User; break;
            }
        }
        return opt;
    }
}
