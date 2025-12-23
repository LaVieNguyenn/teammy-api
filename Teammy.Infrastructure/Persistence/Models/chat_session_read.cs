using System;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class chat_session_read
{
    public Guid chat_session_id { get; set; }

    public Guid user_id { get; set; }

    public DateTime last_read_at { get; set; }

    public Guid? last_read_message_id { get; set; }

    public virtual chat_session chat_session { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
