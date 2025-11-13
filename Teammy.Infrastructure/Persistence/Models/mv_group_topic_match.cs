using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class mv_group_topic_match
{
    public Guid? group_id { get; set; }

    public Guid? semester_id { get; set; }

    public Guid? major_id { get; set; }

    public string? group_name { get; set; }

    public string? group_desc { get; set; }

    public Guid? topic_id { get; set; }

    public string? title { get; set; }

    public string? topic_desc { get; set; }

    public int? simple_score { get; set; }
}
