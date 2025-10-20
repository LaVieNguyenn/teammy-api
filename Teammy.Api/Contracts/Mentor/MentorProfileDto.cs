namespace Teammy.Api.Contracts.Mentor
{
    public sealed record MentorProfileDto(Guid Id, string DisplayName, string Email, IReadOnlyList<string> Skills, string? Bio, IReadOnlyList<object> Availability);
}
