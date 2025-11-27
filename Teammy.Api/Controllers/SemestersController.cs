using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Application.Semesters.Dtos;
using Teammy.Application.Semesters.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/semesters")]
public sealed class SemestersController(SemesterService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Create([FromBody] SemesterUpsertRequest req, CancellationToken ct)
    {
        try
        {
            var id = await service.CreateAsync(req, ct);
            var detail = await service.GetSemesterAsync(id, ct);
            return CreatedAtAction(nameof(GetById), new { id }, detail);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Update([FromRoute] Guid id, [FromBody] SemesterUpsertRequest req, CancellationToken ct)
    {
        try
        {
            await service.UpdateAsync(id, req, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<SemesterSummaryDto>>> List(CancellationToken ct)
    {
        var items = await service.GetSemestersAsync(ct);
        return Ok(items);
    }
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<SemesterDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var d = await service.GetSemesterAsync(id, ct);
        if (d is null) return NotFound();
        return Ok(d);
    }
    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<ActionResult<SemesterDetailDto>> GetActive(CancellationToken ct)
    {
        var d = await service.GetActiveSemesterAsync(ct);
        if (d is null) return NotFound();
        return Ok(d);
    }

    [HttpGet("{id:guid}/policy")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<SemesterPolicyDto>> GetPolicy([FromRoute] Guid id, CancellationToken ct)
    {
        var policy = await service.GetPolicyAsync(id, ct);
        if (policy is null) return NotFound();
        return Ok(policy);
    }

    [HttpPut("{id:guid}/policy")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult> UpsertPolicy(
        [FromRoute] Guid id,
        [FromBody] SemesterPolicyUpsertRequest req,
        CancellationToken ct)
    {
        try
        {
            await service.UpsertPolicyAsync(id, req, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    [HttpPost("{id:guid}/activate")]
     [Authorize(Roles = "admin")]
    public async Task<ActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await service.ActivateAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
