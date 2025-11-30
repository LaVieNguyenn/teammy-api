namespace Teammy.Application.Groups.Dtos;

public sealed class UpdateGroupRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? MaxMembers { get; set; }
    public Guid? MajorId { get; set; }
    public IReadOnlyList<string>? Skills { get; set; }
}
