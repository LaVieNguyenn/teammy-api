namespace Teammy.Domain.Semesters;

public sealed class Semester
{
    public Guid SemesterId { get; set; }
    public string Season { get; set; } = default!;
    public int Year { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public bool IsActive { get; set; }

    public SemesterPolicy? Policy { get; set; }

    public SemesterPhase GetPhase(DateTime nowUtc)
    {
        if (Policy == null) return SemesterPhase.Unknown;

        var p = Policy;
        var today = nowUtc.Date;

        if (today < p.TeamSelfSelectStart.Date) return SemesterPhase.BeforeTeamSelection;
        if (today <= p.TeamSelfSelectEnd.Date) return SemesterPhase.TeamSelfSelection;
        if (today < p.TopicSelfSelectStart.Date) return SemesterPhase.TeamSuggesting;
        if (today <= p.TopicSelfSelectEnd.Date) return SemesterPhase.TopicSelfSelection;
        if (today <= EndDate.Date) return SemesterPhase.TopicSuggesting;

        return SemesterPhase.Finished;
    }
}
