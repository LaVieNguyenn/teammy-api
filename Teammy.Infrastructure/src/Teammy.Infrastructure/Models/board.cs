using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class board
{
    public Guid id { get; set; }

    public Guid group_id { get; set; }

    public string name { get; set; } = null!;

    public DateTime created_at { get; set; }

    public virtual ICollection<board_column> board_columns { get; set; } = new List<board_column>();

    public virtual group group { get; set; } = null!;
}
