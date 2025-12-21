using System.Text.Json;

namespace Teammy.Application.Users.Dtos;

public sealed record UserProfileDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string? Phone,
    string? Gender,
    string? StudentCode,
    Guid? MajorId,
    string? MajorName,
    double? Gpa,
    Guid? DesiredPositionId,
    string? DesiredPositionName,
    JsonElement? Skills,
    bool SkillsCompleted,
    string? AvatarUrl,
    string? PortfolioUrl
);

public sealed record UpdateUserProfileRequest(
    string DisplayName,
    string? Phone,
    string? Gender,
    JsonElement? Skills,
    bool SkillsCompleted,
    string? PortfolioUrl,
    Guid? DesiredPositionId
);
