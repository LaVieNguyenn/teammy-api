using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/majors")]
public sealed class MajorsController(IMajorReadOnlyQueries queries, IMajorWriteRepository writeRepo) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> List(CancellationToken ct)
    {
        var majors = await queries.ListAsync(ct);
        var shaped = majors.Select(m => new { majorId = m.MajorId, majorName = m.MajorName }).ToList();
        return Ok(shaped);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var m = await queries.GetAsync(id, ct);
        if (!m.HasValue) return NotFound();
        var (majorId, majorName) = m.Value;
        return Ok(new { majorId, majorName });
    }


    public sealed record CreateMajorRequest(string Name);
    public sealed record UpdateMajorRequest(string Name);

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Create([FromBody] CreateMajorRequest req, CancellationToken ct)
    {
        try
        {
            var id = await writeRepo.CreateAsync(req.Name, ct);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Update([FromRoute] Guid id, [FromBody] UpdateMajorRequest req, CancellationToken ct)
    {
        try
        {
            await writeRepo.UpdateAsync(id, req.Name, ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            await writeRepo.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
