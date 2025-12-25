using System;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class group_feedback
{
    public Guid feedback_id { get; set; }

    public Guid group_id { get; set; }

    public Guid semester_id { get; set; }

    public Guid mentor_id { get; set; }

    public string? category { get; set; }

    public string summary { get; set; } = null!;

    public string? details { get; set; }

    public int? rating { get; set; }

    public string? blockers { get; set; }

    public string? next_steps { get; set; }

    public string status { get; set; } = null!;

    public Guid? acknowledged_by { get; set; }

    public string? acknowledged_note { get; set; }

    public DateTime? acknowledged_at { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual user? acknowledged_byNavigation { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual user mentor { get; set; } = null!;

    public virtual semester semester { get; set; } = null!;
}
