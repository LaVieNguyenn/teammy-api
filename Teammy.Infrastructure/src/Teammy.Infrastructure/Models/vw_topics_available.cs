using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class vw_topics_available
{
    public Guid? topic_id { get; set; }

    public Guid? term_id { get; set; }

    public string? title { get; set; }

    public string? status { get; set; }

    public long? used_by_groups { get; set; }

    public int? remaining_slots { get; set; }

    public bool? can_take_more { get; set; }
}
