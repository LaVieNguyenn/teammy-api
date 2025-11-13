using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class skill_dictionary
{
    public string token { get; set; } = null!;

    public string role { get; set; } = null!;

    public string major { get; set; } = null!;

    public virtual ICollection<skill_alias> skill_aliases { get; set; } = new List<skill_alias>();
}
