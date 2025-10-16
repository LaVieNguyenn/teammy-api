using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class major
{
    public Guid id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public Guid? department_id { get; set; }

    public virtual department? department { get; set; }

    public virtual ICollection<student_profile> student_profiles { get; set; } = new List<student_profile>();

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();
}
