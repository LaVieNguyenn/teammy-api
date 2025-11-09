using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Users.Dtos;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserReadOnlyQueries users, IGroupReadOnlyQueries groups, ICatalogReadOnlyQueries catalog) : ControllerBase
{
    // Search users by email to invite into a group/semester
    // GET /api/users?email=user@example.com&groupId=...&semesterId=...&limit=20&onlyFree=true
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<UserSearchDto>>> Search(
        [FromQuery] string? email,
        [FromQuery] Guid? groupId,
        [FromQuery] Guid? semesterId,
        [FromQuery] int? limit,
        [FromQuery] bool onlyFree = true,
        CancellationToken ct = default)
    {
        Guid? semId = semesterId;
        if (!semId.HasValue && groupId.HasValue)
        {
            var g = await groups.GetGroupAsync(groupId.Value, ct);
            if (g is null) return NotFound("Group not found");
            semId = g.SemesterId;
        }
        if (!semId.HasValue)
        {
            var active = await catalog.GetActiveSemesterAsync(ct);
            if (active is null) return Conflict("No active semester");
            semId = active.SemesterId;
        }

        var list = await users.SearchInvitableAsync(email, semId.Value, limit ?? 20, ct);
        if (onlyFree)
            list = list.Where(x => !x.HasGroupInSemester).ToList();
        return Ok(list);
    }
}
