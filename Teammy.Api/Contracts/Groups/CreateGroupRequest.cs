namespace Teammy.Api.Contracts.Groups
{
    public sealed class CreateGroupRequest
    {
        public Guid TermId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public Guid? TopicId { get; set; }
        public string? Description { get; set; }
        public string? TechStack { get; set; }
        public string? GithubUrl { get; set; }
    }
}
