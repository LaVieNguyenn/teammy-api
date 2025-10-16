using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class team_auto_run
{
    public Guid id { get; set; }

    public Guid term_id { get; set; }

    public DateTime started_at { get; set; }

    public string strategy { get; set; } = null!;

    public Guid? created_by { get; set; }

    public virtual user? created_byNavigation { get; set; }

    public virtual ICollection<team_auto_result> team_auto_results { get; set; } = new List<team_auto_result>();

    public virtual term term { get; set; } = null!;
}
