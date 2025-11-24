using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Skills.Dtos;
using Teammy.Application.Skills.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/skills")]
public sealed class SkillsController : ControllerBase
{
    private readonly SkillDictionaryService _service;

    public SkillsController(SkillDictionaryService service)
    {
        _service = service;
    }

    [HttpGet]
    // [Authorize]
    public async Task<ActionResult<IReadOnlyList<SkillDictionaryDto>>> List(
        [FromQuery] string? role,
        [FromQuery] string? major,
        CancellationToken ct)
    {
        var items = await _service.ListAsync(role, major, ct);
        return Ok(items);
    }

    [HttpGet("{token}")]
    // [Authorize]
    public async Task<ActionResult<SkillDictionaryDto>> Get(string token, CancellationToken ct)
    {
        var dto = await _service.GetByTokenAsync(token, ct);
        return Ok(dto);
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<SkillDictionaryDto>> Create(
        [FromBody] CreateSkillDictionaryRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { token = dto.Token }, dto);
    }

    [HttpPut("{token}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<SkillDictionaryDto>> Update(
        string token,
        [FromBody] UpdateSkillDictionaryRequest request,
        CancellationToken ct)
    {
        var dto = await _service.UpdateAsync(token, request, ct);
        return Ok(dto);
    }

    [HttpDelete("{token}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(string token, CancellationToken ct)
    {
        await _service.DeleteAsync(token, ct);
        return NoContent();
    }
}
