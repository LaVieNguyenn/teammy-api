namespace Teammy.Application.Groups.Dtos;

public sealed record MyGroupDto(
    Guid   GroupId,
    Guid   SemesterId,
    string Name,
    string Status,
    int    MaxMembers,
    int    CurrentMembers,
    string Role
);

