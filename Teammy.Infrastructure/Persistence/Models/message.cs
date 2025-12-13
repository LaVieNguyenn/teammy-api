using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class message
{
    public Guid message_id { get; set; }

    public Guid chat_session_id { get; set; }

    public Guid sender_id { get; set; }

    public string? type { get; set; }

    public string content { get; set; } = null!;

    public bool is_pinned { get; set; }

    public DateTime? pinned_at { get; set; }

    public Guid? pinned_by { get; set; }

    public bool is_deleted { get; set; }

    public DateTime? deleted_at { get; set; }

    public Guid? deleted_by { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual chat_session chat_session { get; set; } = null!;

    public virtual user sender { get; set; } = null!;
}
