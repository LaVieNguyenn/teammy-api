using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Users.Dtos;
using Teammy.Application.Users.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserReadOnlyQueries _users;
    private readonly IGroupReadOnlyQueries _groups;
    private readonly IUserWriteRepository _userWrite;
    private readonly IRoleReadOnlyQueries _roles;
    private readonly UserProfileService _profileService;

    public UsersController(
        IUserReadOnlyQueries users,
        IGroupReadOnlyQueries groups,
        IUserWriteRepository userWrite,
        IRoleReadOnlyQueries roles,
        UserProfileService profileService)
    {
        _users = users;
        _groups = groups;
        _userWrite = userWrite;
        _roles = roles;
        _profileService = profileService;
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("user_id");
        if (!Guid.TryParse(sub, out var id))
            throw new UnauthorizedAccessException("Invalid token");
        return id;
    }

    [HttpGet("me/profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> GetProfile(CancellationToken ct)
    {
        var dto = await _profileService.GetProfileAsync(GetCurrentUserId(), ct);
        return Ok(dto);
    }

    [HttpGet("{userId:guid}/profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> GetProfileById(Guid userId, CancellationToken ct)
    {
        var dto = await _profileService.GetProfileAsync(userId, ct);
        return Ok(dto);
    }

    [HttpPut("me/profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken ct)
    {
        var dto = await _profileService.UpdateProfileAsync(GetCurrentUserId(), request, ct);
        return Ok(dto);
    }

    [HttpPost("me/avatar")]
    [Authorize]
    [RequestSizeLimit(5_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UserProfileDto>> UpdateAvatar(IFormFile avatar, CancellationToken ct)
    {
        if (avatar is null || avatar.Length == 0)
            return BadRequest("Avatar file is required.");

        if (avatar.Length > 5_000_000)
            return BadRequest("Avatar file must be <= 5MB.");

        await using var stream = avatar.OpenReadStream();
        var dto = await _profileService.UpdateAvatarAsync(GetCurrentUserId(), stream, avatar.FileName, ct);
        return Ok(dto);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<UserSearchDto>>> Search(
        [FromQuery] string? email,
        [FromQuery] Guid? groupId,
        [FromQuery] Guid? semesterId,
        [FromQuery] Guid? majorId,
        [FromQuery] int? limit,
        [FromQuery] bool onlyFree,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Email query is required.");

        Guid? semId = semesterId;
        Guid? major = majorId;

        if (!semId.HasValue && groupId.HasValue)
        {
            var g = await _groups.GetGroupAsync(groupId.Value, ct);
            if (g is null) return NotFound("Group not found");
            semId = g.SemesterId;
            major ??= g.MajorId;
        }

        if (!semId.HasValue)
        {
            semId = await _groups.GetActiveSemesterIdAsync(ct);
            if (!semId.HasValue) return Conflict("No active semester");
        }

        var list = await _users.SearchInvitableAsync(
            email,
            semId.Value,
            major,
            requireStudentRole: true,
            limit: limit ?? 20,
            ct);

        if (onlyFree)
            list = list.Where(x => !x.HasGroupInSemester).ToList();

        return Ok(list);
    }

    [HttpGet("admin")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<IReadOnlyList<AdminUserListItemDto>>> GetAll(CancellationToken ct)
    {
        var list = await _users.GetAllForAdminAsync(ct);
        return Ok(list);
    }

    [HttpGet("admin/{userId:guid}")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AdminUserDetailDto>> GetById(Guid userId, CancellationToken ct)
    {
        var dto = await _users.GetAdminDetailAsync(userId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost("admin")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<AdminUserDetailDto>> Create(
        [FromBody] AdminCreateUserRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Email is required.");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest("DisplayName is required.");
        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest("Role is required.");

        if (await _userWrite.EmailExistsAnyAsync(request.Email, ct))
            return Conflict("Email already exists.");

        var roleId = await _roles.GetRoleIdByNameAsync(request.Role, ct);
        if (!roleId.HasValue)
            return BadRequest("Invalid role.");

        var userId = await _userWrite.CreateUserAsync(
            request.Email,
            request.DisplayName,
            request.StudentCode,
            request.Gender,
            request.MajorId,
            ct);

        await _userWrite.SetSingleRoleAsync(userId, roleId.Value, ct);

        var dto = await _users.GetAdminDetailAsync(userId, ct);
        return CreatedAtAction(nameof(GetById), new { userId }, dto);
    }

    
    [HttpPut("admin/{userId:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(
        Guid userId,
        [FromBody] AdminUpdateUserRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest("DisplayName is required.");
        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest("Role is required.");

        var roleId = await _roles.GetRoleIdByNameAsync(request.Role, ct);
        if (!roleId.HasValue)
            return BadRequest("Invalid role.");

        await _userWrite.UpdateUserAsync(
            userId,
            request.DisplayName,
            request.StudentCode,
            request.Gender,
            request.MajorId,
            request.IsActive,
            ct);

        await _userWrite.SetSingleRoleAsync(userId, roleId.Value, ct);

        return NoContent();
    }

 
    [HttpDelete("admin/{userId:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken ct)
    {
        await _userWrite.DeleteUserAsync(userId, ct);
        return NoContent();
    }

    [HttpGet("admin/roles")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(CancellationToken ct)
    {
        var roles = await _roles.GetAllRoleNamesAsync(ct);
        return Ok(roles);
    }
}
