using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class topic_mentor
{
    public Guid topic_id { get; set; }

    public Guid mentor_id { get; set; }

    public string role_on_topic { get; set; } = null!;

    public virtual user mentor { get; set; } = null!;

    public virtual topic topic { get; set; } = null!;
}
