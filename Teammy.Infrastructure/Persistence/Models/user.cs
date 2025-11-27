using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Persistence.Models;

public partial class user
{
    public Guid user_id { get; set; }

    public string email { get; set; } = null!;

    public bool email_verified { get; set; }

    public string display_name { get; set; } = null!;

    public string? avatar_url { get; set; }

    public string? phone { get; set; }

    public string? student_code { get; set; }

    public string? gender { get; set; }

    public Guid? major_id { get; set; }

    public string? skills { get; set; }

    public bool skills_completed { get; set; }

    public bool is_active { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    public virtual ICollection<announcement> announcements { get; set; } = new List<announcement>();

    public virtual ICollection<backlog_item> backlog_itemcreated_bies { get; set; } = new List<backlog_item>();

    public virtual ICollection<backlog_item> backlog_itemowner_users { get; set; } = new List<backlog_item>();

    public virtual ICollection<candidate> candidateapplicant_users { get; set; } = new List<candidate>();

    public virtual ICollection<candidate> candidateapplied_by_users { get; set; } = new List<candidate>();

    public virtual ICollection<comment> comments { get; set; } = new List<comment>();

    public virtual ICollection<group_member> group_members { get; set; } = new List<group_member>();

    public virtual ICollection<group> groups { get; set; } = new List<group>();

    public virtual ICollection<invitation> invitationinvited_byNavigations { get; set; } = new List<invitation>();

    public virtual ICollection<invitation> invitationinvitee_users { get; set; } = new List<invitation>();

    public virtual major? major { get; set; }

    public virtual ICollection<message> messages { get; set; } = new List<message>();

    public virtual ICollection<milestone> milestone_created_bies { get; set; } = new List<milestone>();

    public virtual ICollection<recruitment_post> recruitment_posts { get; set; } = new List<recruitment_post>();

    public virtual ICollection<shared_file> shared_files { get; set; } = new List<shared_file>();

    public virtual ICollection<task_assignment> task_assignments { get; set; } = new List<task_assignment>();

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();

    public virtual ICollection<user_report> user_reportassigned_toNavigations { get; set; } = new List<user_report>();

    public virtual ICollection<user_report> user_reportreporters { get; set; } = new List<user_report>();

    public virtual ICollection<user_role> user_roles { get; set; } = new List<user_role>();

    public virtual ICollection<topic> topicsNavigation { get; set; } = new List<topic>();

    public virtual ICollection<chat_session_participant> chat_session_participants { get; set; } = new List<chat_session_participant>();
}
