using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Catalog.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/topics")]
public sealed class TopicsController(ICatalogReadOnlyQueries queries) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<TopicDto>> List(CancellationToken ct)
        => queries.ListTopicsAsync(ct);
}

