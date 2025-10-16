using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class vw_admin_overview
{
    public Guid? term_id { get; set; }

    public string? term_code { get; set; }

    public long? total_groups { get; set; }

    public long? topics_open { get; set; }

    public long? topics_total_active { get; set; }

    public long? students_without_group { get; set; }
}
