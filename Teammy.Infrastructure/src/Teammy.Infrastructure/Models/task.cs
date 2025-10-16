using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class task
{
    public Guid id { get; set; }

    public Guid group_id { get; set; }

    public Guid column_id { get; set; }

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public string? priority { get; set; }

    public DateTime? due_at { get; set; }

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual board_column column { get; set; } = null!;

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group group { get; set; } = null!;

    public virtual ICollection<task_comment> task_comments { get; set; } = new List<task_comment>();

    public virtual ICollection<label> labels { get; set; } = new List<label>();

    public virtual ICollection<user> users { get; set; } = new List<user>();
}
