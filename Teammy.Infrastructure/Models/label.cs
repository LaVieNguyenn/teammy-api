using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class label
{
    public Guid id { get; set; }

    public Guid group_id { get; set; }

    public string name { get; set; } = null!;

    public string? color { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual ICollection<task> tasks { get; set; } = new List<task>();
}
