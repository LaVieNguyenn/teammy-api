using System.Text.Json;

namespace Teammy.Application.Auth.Dtos;

public sealed record CurrentUserDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    bool EmailVerified,
    bool SkillsCompleted,
    bool IsActive,
    string Role,
    IReadOnlyList<UserRoleDto> Roles,
    string? Phone,
    string? StudentCode,
    string? Gender,
    Guid? MajorId,
    MajorSummaryDto? Major,
    JsonElement? Skills,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? PortfolioUrl
);

public sealed record UserRoleDto(Guid RoleId, string Name);

public sealed record MajorSummaryDto(Guid MajorId, string Name);
