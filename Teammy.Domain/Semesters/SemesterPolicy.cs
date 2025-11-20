namespace Teammy.Domain.Semesters;

public sealed class SemesterPolicy
{
    public Guid SemesterId { get; set; }

    public DateTime TeamSelfSelectStart { get; set; }
    public DateTime TeamSelfSelectEnd { get; set; }
    public DateTime TeamSuggestStart { get; set; }

    public DateTime TopicSelfSelectStart { get; set; }
    public DateTime TopicSelfSelectEnd { get; set; }
    public DateTime TopicSuggestStart { get; set; }

    public int DesiredGroupSizeMin { get; set; }
    public int DesiredGroupSizeMax { get; set; }

    public Semester Semester { get; set; } = default!;
}
