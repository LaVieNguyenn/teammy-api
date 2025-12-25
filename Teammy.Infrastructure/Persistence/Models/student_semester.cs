using System;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class student_semester
{
    public Guid user_id { get; set; }

    public Guid semester_id { get; set; }

    public bool is_current { get; set; }

    public DateTime created_at { get; set; }

    public virtual semester semester { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
