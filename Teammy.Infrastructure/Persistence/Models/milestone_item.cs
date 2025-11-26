using System;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class milestone_item
{
    public Guid milestone_item_id { get; set; }

    public Guid milestone_id { get; set; }

    public Guid backlog_item_id { get; set; }

    public DateTime added_at { get; set; }

    public virtual backlog_item backlog_item { get; set; } = null!;

    public virtual milestone milestone { get; set; } = null!;
}
