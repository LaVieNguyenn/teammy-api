using System;

namespace Teammy.Application.Users.Dtos;

public sealed record AdminUserListItemDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    bool   EmailVerified,
    bool   IsActive,
    Guid?  MajorId,
    string? MajorName,
    string? StudentCode,
    string? Gender,
    double? Gpa,
    Guid? SemesterId,
    AdminUserSemesterDto? Semester,
    DateTime CreatedAt,
    string? PortfolioUrl
);

public sealed record AdminUserDetailDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    bool   EmailVerified,
    bool   IsActive,
    Guid?  MajorId,
    string? MajorName,
    string? StudentCode,
    string? Gender,
    double? Gpa,
    AdminUserSemesterDto? Semester,
    bool   SkillsCompleted,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? PortfolioUrl
);

public sealed record AdminUserSemesterDto(
    Guid SemesterId,
    string Season,
    int Year,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive
);

public sealed record AdminMajorStatsDto(
    Guid MajorId,
    string MajorName,
    int GroupCount,
    int StudentCount,
    int StudentsWithoutGroup
);
