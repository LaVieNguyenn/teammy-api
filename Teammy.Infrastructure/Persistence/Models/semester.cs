using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class semester
{
    public Guid semester_id { get; set; }

    public string season { get; set; } = null!;

    public int? year { get; set; }

    public DateOnly? start_date { get; set; }

    public DateOnly? end_date { get; set; }

    public bool is_active { get; set; }

    public virtual ICollection<announcement> announcements { get; set; } = new List<announcement>();

    public virtual ICollection<group_member> group_members { get; set; } = new List<group_member>();

    public virtual ICollection<group> groups { get; set; } = new List<group>();

    public virtual ICollection<recruitment_post> recruitment_posts { get; set; } = new List<recruitment_post>();

    public virtual semester_policy? semester_policy { get; set; }

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();

    public virtual ICollection<user_report> user_reports { get; set; } = new List<user_report>();
}
