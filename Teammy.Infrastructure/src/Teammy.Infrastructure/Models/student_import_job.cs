using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class student_import_job
{
    public Guid id { get; set; }

    public Guid? term_id { get; set; }

    public string file_name { get; set; } = null!;

    public int total_rows { get; set; }

    public int success_rows { get; set; }

    public int error_rows { get; set; }

    public string status { get; set; } = null!;

    public string? errors { get; set; }

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual term? term { get; set; }
}
