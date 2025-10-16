using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class announcement
{
    public Guid id { get; set; }

    public Guid? term_id { get; set; }

    public string scope { get; set; } = null!;

    public string? target_role { get; set; }

    public Guid? target_group_id { get; set; }

    public string title { get; set; } = null!;

    public string content { get; set; } = null!;

    public bool pinned { get; set; }

    public DateTime publish_at { get; set; }

    public DateTime? expire_at { get; set; }

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual group? target_group { get; set; }

    public virtual term? term { get; set; }
}
