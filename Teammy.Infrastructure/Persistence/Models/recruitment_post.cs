using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class recruitment_post
{
    public Guid post_id { get; set; }

    public Guid semester_id { get; set; }

    public string post_type { get; set; } = null!;

    public Guid? group_id { get; set; }

    public Guid? user_id { get; set; }

    public Guid? major_id { get; set; }

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public string? position_needed { get; set; }

    public int? current_members { get; set; }

    public string status { get; set; } = null!;

    public DateTime? application_deadline { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual ICollection<candidate> candidates { get; set; } = new List<candidate>();

    public virtual group? group { get; set; }

    public virtual ICollection<invitation> invitations { get; set; } = new List<invitation>();

    public virtual major? major { get; set; }

    public virtual semester semester { get; set; } = null!;

    public virtual user? user { get; set; }
}
