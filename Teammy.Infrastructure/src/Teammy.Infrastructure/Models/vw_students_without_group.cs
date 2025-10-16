using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class vw_students_without_group
{
    public Guid? user_id { get; set; }

    public string? display_name { get; set; }

    public Guid? major_id { get; set; }

    public string? skills { get; set; }

    public Guid? term_id { get; set; }
}
