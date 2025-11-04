using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class mv_group_capacity
{
    public Guid? group_id { get; set; }

    public Guid? semester_id { get; set; }

    public Guid? major_id { get; set; }

    public string? name { get; set; }

    public string? description { get; set; }

    public int? max_members { get; set; }

    public long? current_members { get; set; }

    public long? remaining_slots { get; set; }
}
