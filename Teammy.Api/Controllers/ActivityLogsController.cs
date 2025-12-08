using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/admin/activity-logs")]
[Authorize(Roles = "admin")]
public sealed class ActivityLogsController(ActivityLogService service) : ControllerBase
{
    private readonly ActivityLogService _service = service;

    [HttpGet]
    public async Task<IReadOnlyList<ActivityLogDto>> Get([FromQuery] ActivityLogListRequest request, CancellationToken ct)
        => await _service.GetRecentAsync(request, ct);
}
