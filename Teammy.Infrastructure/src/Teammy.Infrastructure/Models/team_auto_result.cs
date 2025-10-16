using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class team_auto_result
{
    public Guid run_id { get; set; }

    public Guid user_id { get; set; }

    public Guid group_id { get; set; }

    public string? reason { get; set; }

    public DateTime assigned_at { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual team_auto_run run { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
