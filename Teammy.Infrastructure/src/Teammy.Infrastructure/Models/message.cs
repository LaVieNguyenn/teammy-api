using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class message
{
    public Guid id { get; set; }

    public Guid channel_id { get; set; }

    public Guid sender_id { get; set; }

    public string content { get; set; } = null!;

    public string? meta { get; set; }

    public bool is_deleted { get; set; }

    public Guid? deleted_by { get; set; }

    public DateTime? deleted_at { get; set; }

    public DateTime created_at { get; set; }

    public virtual channel channel { get; set; } = null!;

    public virtual user? deleted_byNavigation { get; set; }

    public virtual user sender { get; set; } = null!;
}
