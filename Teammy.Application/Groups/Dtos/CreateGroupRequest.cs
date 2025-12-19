namespace Teammy.Application.Groups.Dtos;

public sealed class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MaxMembers { get; set; }
}
