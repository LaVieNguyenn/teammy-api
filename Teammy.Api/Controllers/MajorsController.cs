using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Catalog.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/majors")]
public sealed class MajorsController(ICatalogReadOnlyQueries queries) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<MajorDto>> List(CancellationToken ct)
        => queries.ListMajorsAsync(ct);
}

