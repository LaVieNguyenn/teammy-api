namespace Teammy.Api.Contracts.Mentor
{
    public sealed record OpenGroupDto(Guid GroupId, string Name, string Status, int Capacity, Guid TopicId, string TopicTitle, string? TopicCode);
}
