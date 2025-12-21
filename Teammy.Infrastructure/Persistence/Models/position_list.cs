using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class position_list
{
    public Guid position_id { get; set; }

    public Guid major_id { get; set; }

    public string position_name { get; set; } = null!;

    public DateTime created_at { get; set; }

    public virtual major major { get; set; } = null!;

    public virtual ICollection<user> users { get; set; } = new List<user>();
}
