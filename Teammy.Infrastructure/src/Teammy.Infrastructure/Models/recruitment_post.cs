using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class recruitment_post
{
    public Guid id { get; set; }

    public Guid term_id { get; set; }

    public string post_kind { get; set; } = null!;

    public Guid? group_id { get; set; }

    public Guid? user_id { get; set; }

    public string title { get; set; } = null!;

    public string? content { get; set; }

    public string? skills { get; set; }

    public int? seats { get; set; }

    public string status { get; set; } = null!;

    public bool is_flagged { get; set; }

    public string? flagged_reason { get; set; }

    public DateTime? expires_at { get; set; }

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group? group { get; set; }

    public virtual ICollection<recruitment_application> recruitment_applications { get; set; } = new List<recruitment_application>();

    public virtual term term { get; set; } = null!;

    public virtual user? user { get; set; }
}
