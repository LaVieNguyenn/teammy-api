using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class topic
{
    public Guid id { get; set; }

    public Guid term_id { get; set; }

    public string? code { get; set; }

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public Guid? department_id { get; set; }

    public Guid? major_id { get; set; }

    public string status { get; set; } = null!;

    public Guid created_by { get; set; }

    public DateTime created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual department? department { get; set; }

    public virtual ICollection<group> groups { get; set; } = new List<group>();

    public virtual major? major { get; set; }

    public virtual term term { get; set; } = null!;

    public virtual ICollection<topic_mentor> topic_mentors { get; set; } = new List<topic_mentor>();

    public virtual ICollection<topic_suggestion> topic_suggestions { get; set; } = new List<topic_suggestion>();
}
