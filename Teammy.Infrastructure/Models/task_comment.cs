using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class task_comment
{
    public Guid id { get; set; }

    public Guid task_id { get; set; }

    public Guid user_id { get; set; }

    public string content { get; set; } = null!;

    public Guid? thread_id { get; set; }

    public Guid? parent_comment_id { get; set; }

    public bool is_resolved { get; set; }

    public Guid? resolved_by { get; set; }

    public DateTime? resolved_at { get; set; }

    public DateTime created_at { get; set; }

    public virtual ICollection<task_comment> Inverseparent_comment { get; set; } = new List<task_comment>();

    public virtual ICollection<task_comment> Inversethread { get; set; } = new List<task_comment>();

    public virtual task_comment? parent_comment { get; set; }

    public virtual user? resolved_byNavigation { get; set; }

    public virtual task task { get; set; } = null!;

    public virtual task_comment? thread { get; set; }

    public virtual user user { get; set; } = null!;
}
