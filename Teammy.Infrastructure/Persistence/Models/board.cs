using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class board
{
    public Guid board_id { get; set; }

    public Guid group_id { get; set; }

    public string board_name { get; set; } = null!;

    public string? status { get; set; }

    public virtual ICollection<column> columns { get; set; } = new List<column>();

    public virtual group group { get; set; } = null!;
}
