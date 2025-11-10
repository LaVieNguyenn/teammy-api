using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class invitation
{
    public Guid invitation_id { get; set; }

    public Guid post_id { get; set; }

    public Guid invitee_user_id { get; set; }

    public Guid invited_by { get; set; }

    public string status { get; set; } = null!;

    public string? message { get; set; }

    public DateTime created_at { get; set; }

    public DateTime? responded_at { get; set; }

    public DateTime? expires_at { get; set; }

    public virtual user invited_byNavigation { get; set; } = null!;

    public virtual user invitee_user { get; set; } = null!;

    public virtual recruitment_post post { get; set; } = null!;
}
