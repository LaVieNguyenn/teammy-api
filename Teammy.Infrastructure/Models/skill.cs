using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class skill
{
    public Guid id { get; set; }

    public string name { get; set; } = null!;

    public virtual ICollection<user> users { get; set; } = new List<user>();
}
