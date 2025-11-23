using System;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class chat_session_participant
{
    public Guid chat_session_id { get; set; }

    public Guid user_id { get; set; }

    public DateTime joined_at { get; set; }

    public virtual chat_session chat_session { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
