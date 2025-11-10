using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class role
{
    public Guid role_id { get; set; }

    public string name { get; set; } = null!;

    public virtual ICollection<user_role> user_roles { get; set; } = new List<user_role>();
}
