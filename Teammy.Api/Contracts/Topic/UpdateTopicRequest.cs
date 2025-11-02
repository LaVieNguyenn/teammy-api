namespace Teammy.Api.Contracts.Topic
{
    public sealed class UpdateTopicRequest
    {
        public string? Title { get; set; }
        public string? Code { get; set; }
        public string? Description { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? MajorId { get; set; }
    }
}
