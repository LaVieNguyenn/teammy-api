using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class channel
{
    public Guid id { get; set; }

    public string type { get; set; } = null!;

    public Guid? group_id { get; set; }

    public string? name { get; set; }

    public string? topic { get; set; }

    public int member_count { get; set; }

    public DateTime? last_message_at { get; set; }

    public virtual ICollection<channel_member> channel_members { get; set; } = new List<channel_member>();

    public virtual direct_conversation? direct_conversation { get; set; }

    public virtual group? group { get; set; }

    public virtual ICollection<message> messages { get; set; } = new List<message>();
}
