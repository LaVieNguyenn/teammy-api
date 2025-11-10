using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class user_role
{
    public Guid user_role_id { get; set; }

    public Guid user_id { get; set; }

    public Guid role_id { get; set; }

    public virtual role role { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
