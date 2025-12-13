using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class topic
{
    public Guid topic_id { get; set; }

    public Guid semester_id { get; set; }

    public Guid? major_id { get; set; }

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public string? source { get; set; }

    public string? skills { get; set; }

    public string? source_file_name { get; set; }

    public string? source_file_type { get; set; }

    public long? source_file_size { get; set; }

    public string status { get; set; } = null!;

    public Guid? pending_group_id { get; set; }

    public DateTime? pending_since { get; set; }

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group? group { get; set; }

    public virtual group? pending_group { get; set; }

    public virtual major? major { get; set; }

    public virtual semester semester { get; set; } = null!;

    public virtual ICollection<user> mentors { get; set; } = new List<user>();

    public virtual ICollection<invitation> invitations { get; set; } = new List<invitation>();
}
