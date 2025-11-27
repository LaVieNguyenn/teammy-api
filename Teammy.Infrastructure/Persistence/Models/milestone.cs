using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class milestone
{
    public Guid milestone_id { get; set; }

    public Guid group_id { get; set; }

    public string name { get; set; } = null!;

    public string? description { get; set; }

    public DateOnly? target_date { get; set; }

    public string status { get; set; } = null!;

    public DateTime? completed_at { get; set; }

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group group { get; set; } = null!;

    public virtual ICollection<milestone_item> milestone_items { get; set; } = new List<milestone_item>();
}
