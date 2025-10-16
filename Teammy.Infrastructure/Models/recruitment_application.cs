using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class recruitment_application
{
    public Guid id { get; set; }

    public Guid post_id { get; set; }

    public Guid applicant_user_id { get; set; }

    public string status { get; set; } = null!;

    public string? message { get; set; }

    public DateTime created_at { get; set; }

    public DateTime? decided_at { get; set; }

    public virtual user applicant_user { get; set; } = null!;

    public virtual recruitment_post post { get; set; } = null!;
}
