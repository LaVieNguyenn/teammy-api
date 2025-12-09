using System;

namespace Teammy.Infrastructure.Persistence.Models;

public class activity_log
{
    public Guid activity_id { get; set; }
    public Guid? group_id { get; set; }
    public string entity_type { get; set; } = string.Empty;
    public Guid? entity_id { get; set; }
    public string action { get; set; } = string.Empty;
    public Guid actor_id { get; set; }
    public Guid? target_user_id { get; set; }
    public string? message { get; set; }
    public string? metadata { get; set; }
    public string status { get; set; } = "success";
    public string? platform { get; set; }
    public string severity { get; set; } = "info";
    public DateTime created_at { get; set; }

    public virtual user? actor { get; set; }
    public virtual user? target_user { get; set; }
    public virtual group? group { get; set; }
}
