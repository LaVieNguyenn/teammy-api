using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class chat_session
{
    public Guid chat_session_id { get; set; }

    public string type { get; set; } = null!;

    public Guid? group_id { get; set; }

    public Guid? participant_a { get; set; }

    public Guid? participant_b { get; set; }

    public int members { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public string? last_message { get; set; }

    public virtual group? group { get; set; }

    public virtual ICollection<message> messages { get; set; } = new List<message>();

    public virtual ICollection<chat_session_participant> chat_session_participants { get; set; } = new List<chat_session_participant>();
}
