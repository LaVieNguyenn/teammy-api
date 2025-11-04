using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class group_member
{
    public Guid group_member_id { get; set; }

    public Guid group_id { get; set; }

    public Guid user_id { get; set; }

    public Guid semester_id { get; set; }

    public string status { get; set; } = null!;

    public DateTime joined_at { get; set; }

    public DateTime? left_at { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual semester semester { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
