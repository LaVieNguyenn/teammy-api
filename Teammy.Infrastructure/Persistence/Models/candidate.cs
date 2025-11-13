using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class candidate
{
    public Guid candidate_id { get; set; }

    public Guid post_id { get; set; }

    public Guid? applicant_user_id { get; set; }

    public Guid? applicant_group_id { get; set; }

    public Guid? applied_by_user_id { get; set; }

    public string? message { get; set; }

    public string status { get; set; } = null!;

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual group? applicant_group { get; set; }

    public virtual user? applicant_user { get; set; }

    public virtual user? applied_by_user { get; set; }

    public virtual recruitment_post post { get; set; } = null!;
}
