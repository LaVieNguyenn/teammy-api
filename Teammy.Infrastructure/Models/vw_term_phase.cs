using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class vw_term_phase
{
    public Guid? term_id { get; set; }

    public string? term_code { get; set; }

    public DateOnly? today { get; set; }

    public DateOnly? team_self_select_start { get; set; }

    public DateOnly? team_self_select_end { get; set; }

    public DateOnly? team_suggest_start { get; set; }

    public DateOnly? topic_self_select_start { get; set; }

    public DateOnly? topic_self_select_end { get; set; }

    public DateOnly? topic_suggest_start { get; set; }

    public int? desired_group_size_min { get; set; }

    public int? desired_group_size_max { get; set; }

    public string? team_phase { get; set; }

    public string? topic_phase { get; set; }
}
