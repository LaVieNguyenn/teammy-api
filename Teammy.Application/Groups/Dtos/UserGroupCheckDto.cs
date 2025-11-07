namespace Teammy.Application.Groups.Dtos;

public sealed record UserGroupCheckDto(
    bool   HasGroup,
    Guid   SemesterId,
    Guid?  GroupId,
    string? Status
);

