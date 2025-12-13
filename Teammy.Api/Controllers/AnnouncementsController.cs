using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Announcements.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/announcements")]
public sealed class AnnouncementsController(AnnouncementService service) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public Task<IReadOnlyList<AnnouncementDto>> List([FromQuery] bool includeExpired, [FromQuery] bool pinnedOnly, CancellationToken ct)
        => service.ListAsync(GetUserId(), new AnnouncementFilter(includeExpired, pinnedOnly), ct);

    [HttpGet("{id:guid}")]
    [Authorize]
    public Task<AnnouncementDto?> Get(Guid id, CancellationToken ct)
        => service.GetAsync(id, GetUserId(), ct);

    [HttpPost]
    [Authorize(Roles = "mentor,moderator,admin")]
    public async Task<ActionResult<AnnouncementDto>> Create([FromBody] CreateAnnouncementRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("recipients-preview")]
    [Authorize(Roles = "mentor,moderator,admin")]
    public async Task<ActionResult<AnnouncementRecipientPreviewDto>> PreviewRecipients([FromQuery] AnnouncementRecipientPreviewRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var preview = await service.PreviewRecipientsAsync(GetUserId(), request, ct);
        return Ok(preview);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("user_id");
        if (!Guid.TryParse(sub, out var id))
            throw new UnauthorizedAccessException("Invalid token");
        return id;
    }
}
