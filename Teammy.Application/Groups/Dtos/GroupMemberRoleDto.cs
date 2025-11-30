namespace Teammy.Application.Groups.Dtos;

public sealed record GroupMemberRoleDto(
    Guid GroupMemberRoleId,
    Guid GroupMemberId,
    Guid MemberUserId,
    string MemberDisplayName,
    string RoleName,
    Guid? AssignedByUserId,
    string? AssignedByDisplayName,
    DateTime AssignedAt);
