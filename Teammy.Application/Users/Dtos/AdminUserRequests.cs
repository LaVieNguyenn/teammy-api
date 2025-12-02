using System;

namespace Teammy.Application.Users.Dtos;

public sealed record AdminCreateUserRequest(
    string Email,
    string DisplayName,
    string Role,
    string? StudentCode,
    string? Gender,
    Guid? MajorId
);

public sealed record AdminUpdateUserRequest(
    string DisplayName,
    string Role,
    string? StudentCode,
    string? Gender,
    Guid? MajorId,
    bool IsActive,
    string? PortfolioUrl
);
