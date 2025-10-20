namespace Teammy.Api.Contracts.Mentor
{
    public sealed record AssignedGroupDto(Guid GroupId, string Name, string Status, int Capacity, Guid TopicId, string TopicTitle, string? TopicCode);
}
