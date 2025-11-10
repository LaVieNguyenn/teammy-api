using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class column
{
    public Guid column_id { get; set; }

    public Guid board_id { get; set; }

    public string column_name { get; set; } = null!;

    public int position { get; set; }

    public bool is_done { get; set; }

    public DateTime? due_date { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual board board { get; set; } = null!;

    public virtual ICollection<task> tasks { get; set; } = new List<task>();
}
