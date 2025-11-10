using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class shared_file
{
    public Guid file_id { get; set; }

    public Guid group_id { get; set; }

    public Guid uploaded_by { get; set; }

    public Guid? task_id { get; set; }

    public string file_url { get; set; } = null!;

    public string? file_type { get; set; }

    public long? file_size { get; set; }

    public string? description { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual group group { get; set; } = null!;

    public virtual task? task { get; set; }

    public virtual user uploaded_byNavigation { get; set; } = null!;
}
