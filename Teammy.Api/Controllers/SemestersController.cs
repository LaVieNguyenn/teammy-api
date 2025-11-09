using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Catalog.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/semesters")]
public sealed class SemestersController(ICatalogReadOnlyQueries queries) : ControllerBase
{
    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<ActionResult<SemesterDto>> GetActive(CancellationToken ct)
    {
        var s = await queries.GetActiveSemesterAsync(ct);
        return s is null ? NotFound() : Ok(s);
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<SemesterDto>> List(CancellationToken ct)
        => queries.ListSemestersAsync(ct);
}

