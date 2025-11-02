namespace Teammy.Api.Contracts.Topic
{
    public sealed class TopicDto
    {
        public Guid Id { get; set; }
        public Guid TermId { get; set; }
        public string? Code { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? MajorId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
