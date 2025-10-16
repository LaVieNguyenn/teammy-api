using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class channel_member
{
    public Guid channel_id { get; set; }

    public Guid user_id { get; set; }

    public string role_in_channel { get; set; } = null!;

    public bool is_muted { get; set; }

    public DateTime? last_read_at { get; set; }

    public virtual channel channel { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
