using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class user_report
{
    public Guid report_id { get; set; }

    public Guid reporter_id { get; set; }

    public string target_type { get; set; } = null!;

    public Guid target_id { get; set; }

    public Guid? semester_id { get; set; }

    public string? reason { get; set; }

    public string status { get; set; } = null!;

    public Guid? assigned_to { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual user? assigned_toNavigation { get; set; }

    public virtual user reporter { get; set; } = null!;

    public virtual semester? semester { get; set; }
}
