using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class group
{
    public Guid group_id { get; set; }

    public Guid semester_id { get; set; }

    public Guid? topic_id { get; set; }

    public Guid? mentor_id { get; set; }

    public Guid? major_id { get; set; }

    public string name { get; set; } = null!;

    public string? description { get; set; }

    public int max_members { get; set; }

    public string status { get; set; } = null!;

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual ICollection<announcement> announcements { get; set; } = new List<announcement>();

    public virtual board? board { get; set; }

    public virtual ICollection<candidate> candidates { get; set; } = new List<candidate>();

    public virtual chat_session? chat_session { get; set; }

    public virtual group_member? group_member { get; set; }

    public virtual major? major { get; set; }

    public virtual user? mentor { get; set; }

    public virtual ICollection<recruitment_post> recruitment_posts { get; set; } = new List<recruitment_post>();

    public virtual semester semester { get; set; } = null!;

    public virtual ICollection<shared_file> shared_files { get; set; } = new List<shared_file>();

    public virtual ICollection<task> tasks { get; set; } = new List<task>();

    public virtual topic? topic { get; set; }
}
