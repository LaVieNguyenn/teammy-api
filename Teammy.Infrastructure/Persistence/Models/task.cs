using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class task
{
    public Guid task_id { get; set; }

    public Guid group_id { get; set; }

    public Guid column_id { get; set; }

    public Guid? backlog_item_id { get; set; }

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public string? priority { get; set; }

    public string? status { get; set; }

    public DateTime? due_date { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public decimal sort_order { get; set; }

    public virtual column column { get; set; } = null!;

    public virtual ICollection<comment> comments { get; set; } = new List<comment>();

    public virtual group group { get; set; } = null!;

    public virtual ICollection<shared_file> shared_files { get; set; } = new List<shared_file>();

    public virtual ICollection<task_assignment> task_assignments { get; set; } = new List<task_assignment>();

    public virtual backlog_item? backlog_item { get; set; }
}
