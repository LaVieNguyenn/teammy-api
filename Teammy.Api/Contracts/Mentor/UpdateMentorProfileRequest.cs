namespace Teammy.Api.Contracts.Mentor
{
    public sealed class UpdateMentorProfileRequest
    {
        public string? Bio { get; set; }
        public List<string>? Skills { get; set; }
        public List<object>? Availability { get; set; }
    }
}
