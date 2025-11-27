using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class backlog_item
{
    public Guid backlog_item_id { get; set; }

    public Guid group_id { get; set; }

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public string? priority { get; set; }

    public string status { get; set; } = null!;

    public string? category { get; set; }

    public int? story_points { get; set; }

    public Guid? owner_user_id { get; set; }

    public Guid created_by { get; set; }

    public DateTime? due_date { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual ICollection<milestone_item> milestone_items { get; set; } = new List<milestone_item>();

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group group { get; set; } = null!;

    public virtual user? owner_user { get; set; }

    public virtual ICollection<task> tasks { get; set; } = new List<task>();
}
