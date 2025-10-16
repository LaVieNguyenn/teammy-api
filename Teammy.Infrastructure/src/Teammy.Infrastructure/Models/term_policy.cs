using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class term_policy
{
    public Guid term_id { get; set; }

    public DateOnly team_self_select_start { get; set; }

    public DateOnly team_self_select_end { get; set; }

    public DateOnly team_suggest_start { get; set; }

    public DateOnly topic_self_select_start { get; set; }

    public DateOnly topic_self_select_end { get; set; }

    public DateOnly topic_suggest_start { get; set; }

    public int desired_group_size_min { get; set; }

    public int desired_group_size_max { get; set; }

    public virtual term term { get; set; } = null!;
}
