using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class comment
{
    public Guid comment_id { get; set; }

    public Guid task_id { get; set; }

    public Guid user_id { get; set; }

    public string content { get; set; } = null!;

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual task task { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
