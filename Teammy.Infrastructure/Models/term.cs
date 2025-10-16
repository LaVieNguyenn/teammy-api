using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class term
{
    public Guid id { get; set; }

    public string code { get; set; } = null!;

    public DateOnly start_date_utc { get; set; }

    public DateOnly end_date_utc { get; set; }

    public bool is_active { get; set; }

    public virtual ICollection<announcement> announcements { get; set; } = new List<announcement>();

    public virtual ICollection<group_member> group_members { get; set; } = new List<group_member>();

    public virtual ICollection<group> groups { get; set; } = new List<group>();

    public virtual ICollection<invitation> invitations { get; set; } = new List<invitation>();

    public virtual ICollection<recruitment_post> recruitment_posts { get; set; } = new List<recruitment_post>();

    public virtual ICollection<student_import_job> student_import_jobs { get; set; } = new List<student_import_job>();

    public virtual ICollection<team_auto_run> team_auto_runs { get; set; } = new List<team_auto_run>();

    public virtual ICollection<team_suggestion> team_suggestions { get; set; } = new List<team_suggestion>();

    public virtual term_import_status? term_import_status { get; set; }

    public virtual term_policy? term_policy { get; set; }

    public virtual ICollection<topic_import_job> topic_import_jobs { get; set; } = new List<topic_import_job>();

    public virtual ICollection<topic_suggestion> topic_suggestions { get; set; } = new List<topic_suggestion>();

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();

    public virtual ICollection<user_report> user_reports { get; set; } = new List<user_report>();
}
