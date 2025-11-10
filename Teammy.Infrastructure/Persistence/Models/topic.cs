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

    public string status { get; set; } = null!;

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group? group { get; set; }

    public virtual major? major { get; set; }

    public virtual semester semester { get; set; } = null!;

    public virtual ICollection<user> mentors { get; set; } = new List<user>();
}
