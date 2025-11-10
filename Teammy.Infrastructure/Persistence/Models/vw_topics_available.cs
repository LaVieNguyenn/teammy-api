using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class vw_topics_available
{
    public Guid? topic_id { get; set; }

    public Guid? semester_id { get; set; }

    public Guid? major_id { get; set; }

    public string? title { get; set; }

    public string? description { get; set; }

    public string? status { get; set; }

    public long? used_by_groups { get; set; }

    public bool? can_take_more { get; set; }
}
