using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class group
{
    public Guid id { get; set; }

    public Guid term_id { get; set; }

    public Guid? topic_id { get; set; }

    public string name { get; set; } = null!;

    public int capacity { get; set; }

    public string status { get; set; } = null!;

    public DateTime created_at { get; set; }

    public virtual ICollection<announcement> announcements { get; set; } = new List<announcement>();

    public virtual board? board { get; set; }

    public virtual channel? channel { get; set; }

    public virtual group_member? group_member { get; set; }

    public virtual ICollection<invitation> invitations { get; set; } = new List<invitation>();

    public virtual ICollection<label> labels { get; set; } = new List<label>();

    public virtual ICollection<recruitment_post> recruitment_posts { get; set; } = new List<recruitment_post>();

    public virtual ICollection<task> tasks { get; set; } = new List<task>();

    public virtual ICollection<team_auto_result> team_auto_results { get; set; } = new List<team_auto_result>();

    public virtual ICollection<team_suggestion> team_suggestions { get; set; } = new List<team_suggestion>();

    public virtual term term { get; set; } = null!;

    public virtual topic? topic { get; set; }
}
