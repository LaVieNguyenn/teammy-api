using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class vw_groups_without_topic
{
    public Guid? group_id { get; set; }

    public Guid? term_id { get; set; }

    public string? name { get; set; }

    public int? capacity { get; set; }
}
