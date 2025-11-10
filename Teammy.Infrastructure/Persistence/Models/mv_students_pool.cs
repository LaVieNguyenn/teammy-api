using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class mv_students_pool
{
    public Guid? user_id { get; set; }

    public string? display_name { get; set; }

    public Guid? major_id { get; set; }

    public Guid? semester_id { get; set; }

    public string? skills { get; set; }

    public string? primary_role { get; set; }

    public bool? skills_completed { get; set; }
}
