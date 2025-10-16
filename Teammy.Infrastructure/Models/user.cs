using System;
using System.Collections.Generic;

namespace Teammy.Infrastructure.Models;

public partial class user
{
    public Guid id { get; set; }

    public string email { get; set; } = null!;

    public bool email_verified { get; set; }

    public string display_name { get; set; } = null!;

    public string? photo_url { get; set; }

    public bool is_active { get; set; }

    public Guid role_id { get; set; }

    public string? password_hash { get; set; }

    public DateTime created_at { get; set; }

    public virtual ICollection<announcement> announcements { get; set; } = new List<announcement>();

    public virtual ICollection<channel_member> channel_members { get; set; } = new List<channel_member>();

    public virtual ICollection<direct_conversation> direct_conversationuser1s { get; set; } = new List<direct_conversation>();

    public virtual ICollection<direct_conversation> direct_conversationuser2s { get; set; } = new List<direct_conversation>();

    public virtual ICollection<group_member> group_members { get; set; } = new List<group_member>();

    public virtual ICollection<invitation> invitationinvited_by_users { get; set; } = new List<invitation>();

    public virtual ICollection<invitation> invitationinvitee_users { get; set; } = new List<invitation>();

    public virtual ICollection<message> messagedeleted_byNavigations { get; set; } = new List<message>();

    public virtual ICollection<message> messagesenders { get; set; } = new List<message>();

    public virtual ICollection<recruitment_application> recruitment_applications { get; set; } = new List<recruitment_application>();

    public virtual ICollection<recruitment_post> recruitment_postcreated_byNavigations { get; set; } = new List<recruitment_post>();

    public virtual ICollection<recruitment_post> recruitment_postusers { get; set; } = new List<recruitment_post>();

    public virtual role role { get; set; } = null!;

    public virtual ICollection<student_import_job> student_import_jobs { get; set; } = new List<student_import_job>();

    public virtual student_profile? student_profile { get; set; }

    public virtual ICollection<task_comment> task_commentresolved_byNavigations { get; set; } = new List<task_comment>();

    public virtual ICollection<task_comment> task_commentusers { get; set; } = new List<task_comment>();

    public virtual ICollection<task> tasks { get; set; } = new List<task>();

    public virtual ICollection<team_auto_result> team_auto_results { get; set; } = new List<team_auto_result>();

    public virtual ICollection<team_auto_run> team_auto_runs { get; set; } = new List<team_auto_run>();

    public virtual ICollection<team_suggestion> team_suggestions { get; set; } = new List<team_suggestion>();

    public virtual ICollection<topic_import_job> topic_import_jobs { get; set; } = new List<topic_import_job>();

    public virtual ICollection<topic_mentor> topic_mentors { get; set; } = new List<topic_mentor>();

    public virtual ICollection<topic> topics { get; set; } = new List<topic>();

    public virtual ICollection<user_report> user_reportassigned_toNavigations { get; set; } = new List<user_report>();

    public virtual ICollection<user_report> user_reportreporters { get; set; } = new List<user_report>();

    public virtual ICollection<skill> skills { get; set; } = new List<skill>();

    public virtual ICollection<task> tasksNavigation { get; set; } = new List<task>();
}
