using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class major
{
    public Guid major_id { get; set; }

    public string major_name { get; set; } = null!;

    public virtual ICollection<group> groups { get; set; } = new List<group>();

    public virtual ICollection<recruitment_post> recruitment_posts { get; set; } = new List<recruitment_post>();

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();

    public virtual ICollection<user> users { get; set; } = new List<user>();
}
