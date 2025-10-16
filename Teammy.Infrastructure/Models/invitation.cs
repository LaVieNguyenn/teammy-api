using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class invitation
{
    public Guid id { get; set; }

    public Guid group_id { get; set; }

    public Guid invitee_user_id { get; set; }

    public Guid term_id { get; set; }

    public string status { get; set; } = null!;

    public Guid invited_by_user_id { get; set; }

    public string? message { get; set; }

    public DateTime created_at { get; set; }

    public DateTime? responded_at { get; set; }

    public DateTime? expires_at { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual user invited_by_user { get; set; } = null!;

    public virtual user invitee_user { get; set; } = null!;

    public virtual term term { get; set; } = null!;
}
