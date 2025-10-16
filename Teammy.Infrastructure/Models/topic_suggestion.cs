using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class topic_suggestion
{
    public string subject_type { get; set; } = null!;

    public Guid subject_id { get; set; }

    public Guid term_id { get; set; }

    public Guid topic_id { get; set; }

    public decimal score { get; set; }

    public string? reasons { get; set; }

    public DateTime computed_at { get; set; }

    public virtual term term { get; set; } = null!;

    public virtual topic topic { get; set; } = null!;
}
