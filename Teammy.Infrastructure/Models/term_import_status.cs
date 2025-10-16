using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class term_import_status
{
    public Guid term_id { get; set; }

    public bool mentors_ready { get; set; }

    public bool topics_ready { get; set; }

    public DateTime updated_at { get; set; }

    public virtual term term { get; set; } = null!;
}
