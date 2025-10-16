using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class team_suggestion
{
    public Guid user_id { get; set; }

    public Guid term_id { get; set; }

    public Guid group_id { get; set; }

    public decimal score { get; set; }

    public string? reasons { get; set; }

    public DateTime computed_at { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual term term { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
