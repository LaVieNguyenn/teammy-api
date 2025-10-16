using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.src.Teammy.Infrastructure.Models;

public partial class department
{
    public Guid id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public virtual ICollection<major> majors { get; set; } = new List<major>();

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();
}
