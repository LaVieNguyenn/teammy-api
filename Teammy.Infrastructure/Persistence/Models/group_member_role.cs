using System;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class group_member_role
{
    public Guid group_member_role_id { get; set; }

    public Guid group_member_id { get; set; }

    public string role_name { get; set; } = null!;

    public Guid? assigned_by { get; set; }

    public DateTime assigned_at { get; set; }
}
