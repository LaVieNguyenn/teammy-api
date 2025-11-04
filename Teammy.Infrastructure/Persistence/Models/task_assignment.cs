using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class task_assignment
{
    public Guid task_assignment_id { get; set; }

    public Guid task_id { get; set; }

    public Guid user_id { get; set; }

    public DateTime assigned_at { get; set; }

    public virtual task task { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
