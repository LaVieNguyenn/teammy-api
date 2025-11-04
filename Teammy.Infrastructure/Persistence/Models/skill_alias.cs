using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class skill_alias
{
    public string alias { get; set; } = null!;

    public string token { get; set; } = null!;

    public virtual skill_dictionary tokenNavigation { get; set; } = null!;
}
