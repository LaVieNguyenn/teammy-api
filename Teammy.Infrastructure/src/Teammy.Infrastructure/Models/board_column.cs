using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class board_column
{
    public Guid id { get; set; }

    public Guid board_id { get; set; }

    public string name { get; set; } = null!;

    public int position { get; set; }

    public bool is_done { get; set; }

    public int? wip_limit { get; set; }

    public virtual board board { get; set; } = null!;

    public virtual ICollection<task> tasks { get; set; } = new List<task>();
}
