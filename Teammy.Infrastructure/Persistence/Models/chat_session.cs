using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class chat_session
{
    public Guid chat_session_id { get; set; }

    public string type { get; set; } = null!;

    public Guid? group_id { get; set; }

    public int members { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public string? last_message { get; set; }

    public virtual group? group { get; set; }

    public virtual ICollection<message> messages { get; set; } = new List<message>();
}
