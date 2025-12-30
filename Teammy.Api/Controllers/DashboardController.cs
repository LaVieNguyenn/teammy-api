using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Dashboard.Dtos;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(IDashboardReadOnlyQueries queries) : ControllerBase
{
    [HttpGet]
    public Task<DashboardStatsDto> Get([FromQuery] Guid? semesterId, CancellationToken ct)
        => queries.GetStatsAsync(semesterId, ct);

    [HttpGet("moderator")]
    [Authorize(Roles = "moderator")]
    public Task<ModeratorDashboardStatsDto> GetModerator([FromQuery] Guid? semesterId, CancellationToken ct)
        => queries.GetModeratorStatsAsync(semesterId, ct);
}
