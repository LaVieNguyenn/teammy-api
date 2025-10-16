using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class student_profile
{
    public Guid user_id { get; set; }

    public string? full_name { get; set; }

    public DateOnly? date_of_birth { get; set; }

    public string? student_code { get; set; }

    public string? phone_number { get; set; }

    public string? avatar_url { get; set; }

    public string? gender { get; set; }

    public int? cohort_year { get; set; }

    public string? class_name { get; set; }

    public Guid? major_id { get; set; }

    public string? skills { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual major? major { get; set; }

    public virtual user user { get; set; } = null!;
}
