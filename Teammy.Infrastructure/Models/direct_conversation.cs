using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class direct_conversation
{
    public Guid channel_id { get; set; }

    public Guid user1_id { get; set; }

    public Guid user2_id { get; set; }

    public virtual channel channel { get; set; } = null!;

    public virtual user user1 { get; set; } = null!;

    public virtual user user2 { get; set; } = null!;
}
