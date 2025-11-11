using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Teammy.Application.Topics.Dtos;
using Teammy.Application.Topics.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/topics")]
public sealed class TopicsController(TopicService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) throw new UnauthorizedAccessException("Invalid token");
        return userId;
    }

    [HttpGet]
    public Task<IReadOnlyList<TopicListItemDto>> GetAll(
        [FromQuery] string? q,
        [FromQuery] Guid? semesterId,
        [FromQuery] string? status,
        [FromQuery] Guid? majorId,
        CancellationToken ct)
        => service.GetAllAsync(q, semesterId, status?.Trim().ToLowerInvariant(), majorId, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TopicDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var dto = await service.GetByIdAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateTopicRequest req, CancellationToken ct)
    {
        var id = await service.CreateAsync(GetUserId(), req, ct);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateTopicRequest req, CancellationToken ct)
    {
        await service.UpdateAsync(id, req, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("import/template")]
    [Authorize]
    public async Task<IActionResult> Template(CancellationToken ct)
    {
        var bytes = await service.BuildTemplateAsync(ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "TeammyTopicsTemplate.xlsx");
    }

    [HttpPost("import")]
    [Authorize]
    [Consumes("multipart/form-data")] 
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is required.");
        await using var s = file.OpenReadStream();
        var result = await service.ImportAsync(GetUserId(), s, ct);
        return Ok(result);
    }
}
