using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Teammy.Infrastructure.Models;

namespace Teammy.Infrastructure.Persistence;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<announcement> announcements { get; set; }

    public virtual DbSet<board> boards { get; set; }

    public virtual DbSet<board_column> board_columns { get; set; }

    public virtual DbSet<channel> channels { get; set; }

    public virtual DbSet<channel_member> channel_members { get; set; }

    public virtual DbSet<department> departments { get; set; }

    public virtual DbSet<direct_conversation> direct_conversations { get; set; }

    public virtual DbSet<group> groups { get; set; }

    public virtual DbSet<group_member> group_members { get; set; }

    public virtual DbSet<invitation> invitations { get; set; }

    public virtual DbSet<label> labels { get; set; }

    public virtual DbSet<major> majors { get; set; }

    public virtual DbSet<message> messages { get; set; }

    public virtual DbSet<recruitment_application> recruitment_applications { get; set; }

    public virtual DbSet<recruitment_post> recruitment_posts { get; set; }

    public virtual DbSet<role> roles { get; set; }

    public virtual DbSet<skill> skills { get; set; }

    public virtual DbSet<student_import_job> student_import_jobs { get; set; }

    public virtual DbSet<student_profile> student_profiles { get; set; }

    public virtual DbSet<task> tasks { get; set; }

    public virtual DbSet<task_comment> task_comments { get; set; }

    public virtual DbSet<team_auto_result> team_auto_results { get; set; }

    public virtual DbSet<team_auto_run> team_auto_runs { get; set; }

    public virtual DbSet<team_suggestion> team_suggestions { get; set; }

    public virtual DbSet<term> terms { get; set; }

    public virtual DbSet<term_import_status> term_import_statuses { get; set; }

    public virtual DbSet<term_policy> term_policies { get; set; }

    public virtual DbSet<topic> topics { get; set; }

    public virtual DbSet<topic_import_job> topic_import_jobs { get; set; }

    public virtual DbSet<topic_mentor> topic_mentors { get; set; }

    public virtual DbSet<topic_suggestion> topic_suggestions { get; set; }

    public virtual DbSet<user> users { get; set; }

    public virtual DbSet<user_report> user_reports { get; set; }

    public virtual DbSet<vw_admin_overview> vw_admin_overviews { get; set; }

    public virtual DbSet<vw_groups_without_topic> vw_groups_without_topics { get; set; }

    public virtual DbSet<vw_students_without_group> vw_students_without_groups { get; set; }

    public virtual DbSet<vw_term_phase> vw_term_phases { get; set; }

    public virtual DbSet<vw_topics_available> vw_topics_availables { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresExtension("citext")
            .HasPostgresExtension("pgcrypto")
            .HasPostgresExtension("unaccent");

        modelBuilder.Entity<announcement>(entity =>
        {
            entity.HasKey(e => e.id).HasName("announcements_pkey");

            entity.ToTable("announcements", "teammy");

            entity.HasIndex(e => e.target_group_id, "ix_ann_group");

            entity.HasIndex(e => new { e.scope, e.publish_at }, "ix_ann_scope_time").IsDescending(false, true);

            entity.HasIndex(e => e.term_id, "ix_ann_term");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.pinned).HasDefaultValue(false);
            entity.Property(e => e.publish_at).HasDefaultValueSql("now()");
            entity.Property(e => e.scope).HasDefaultValueSql("'term'::text");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.announcements)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("announcements_created_by_fkey");

            entity.HasOne(d => d.target_group).WithMany(p => p.announcements)
                .HasForeignKey(d => d.target_group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("announcements_target_group_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.announcements)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("announcements_term_id_fkey");
        });

        modelBuilder.Entity<board>(entity =>
        {
            entity.HasKey(e => e.id).HasName("boards_pkey");

            entity.ToTable("boards", "teammy");

            entity.HasIndex(e => e.group_id, "boards_group_id_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.name).HasDefaultValueSql("'Board'::text");

            entity.HasOne(d => d.group).WithOne(p => p.board)
                .HasForeignKey<board>(d => d.group_id)
                .HasConstraintName("boards_group_id_fkey");
        });

        modelBuilder.Entity<board_column>(entity =>
        {
            entity.HasKey(e => e.id).HasName("board_columns_pkey");

            entity.ToTable("board_columns", "teammy");

            entity.HasIndex(e => new { e.board_id, e.name }, "board_columns_board_id_name_key").IsUnique();

            entity.HasIndex(e => new { e.board_id, e.position }, "board_columns_board_id_position_key").IsUnique();

            entity.HasIndex(e => new { e.board_id, e.is_done }, "ix_board_columns_board_done");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.is_done).HasDefaultValue(false);

            entity.HasOne(d => d.board).WithMany(p => p.board_columns)
                .HasForeignKey(d => d.board_id)
                .HasConstraintName("board_columns_board_id_fkey");
        });

        modelBuilder.Entity<channel>(entity =>
        {
            entity.HasKey(e => e.id).HasName("channels_pkey");

            entity.ToTable("channels", "teammy");

            entity.HasIndex(e => e.last_message_at, "ix_channels_last_message_at").IsDescending();

            entity.HasIndex(e => e.group_id, "ux_channels_project_single")
                .IsUnique()
                .HasFilter("(type = 'project'::text)");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.member_count).HasDefaultValue(0);
            entity.Property(e => e.type).HasDefaultValueSql("'group'::text");

            entity.HasOne(d => d.group).WithOne(p => p.channel)
                .HasForeignKey<channel>(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("channels_group_id_fkey");
        });

        modelBuilder.Entity<channel_member>(entity =>
        {
            entity.HasKey(e => new { e.channel_id, e.user_id }).HasName("channel_members_pkey");

            entity.ToTable("channel_members", "teammy");

            entity.HasIndex(e => e.user_id, "ix_channel_members_user");

            entity.Property(e => e.is_muted).HasDefaultValue(false);
            entity.Property(e => e.role_in_channel).HasDefaultValueSql("'member'::text");

            entity.HasOne(d => d.channel).WithMany(p => p.channel_members)
                .HasForeignKey(d => d.channel_id)
                .HasConstraintName("channel_members_channel_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.channel_members)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("channel_members_user_id_fkey");
        });

        modelBuilder.Entity<department>(entity =>
        {
            entity.HasKey(e => e.id).HasName("departments_pkey");

            entity.ToTable("departments", "teammy");

            entity.HasIndex(e => e.code, "departments_code_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<direct_conversation>(entity =>
        {
            entity.HasKey(e => e.channel_id).HasName("direct_conversations_pkey");

            entity.ToTable("direct_conversations", "teammy");

            entity.HasIndex(e => new { e.user1_id, e.user2_id }, "uq_dc_pair").IsUnique();

            entity.Property(e => e.channel_id).ValueGeneratedNever();

            entity.HasOne(d => d.channel).WithOne(p => p.direct_conversation)
                .HasForeignKey<direct_conversation>(d => d.channel_id)
                .HasConstraintName("direct_conversations_channel_id_fkey");

            entity.HasOne(d => d.user1).WithMany(p => p.direct_conversationuser1s)
                .HasForeignKey(d => d.user1_id)
                .HasConstraintName("direct_conversations_user1_id_fkey");

            entity.HasOne(d => d.user2).WithMany(p => p.direct_conversationuser2s)
                .HasForeignKey(d => d.user2_id)
                .HasConstraintName("direct_conversations_user2_id_fkey");
        });

        modelBuilder.Entity<group>(entity =>
        {
            entity.HasKey(e => e.id).HasName("groups_pkey");

            entity.ToTable("groups", "teammy");

            entity.HasIndex(e => new { e.term_id, e.name }, "groups_term_id_name_key").IsUnique();

            entity.HasIndex(e => e.topic_id, "ix_groups_topic");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'recruiting'::text");

            entity.HasOne(d => d.term).WithMany(p => p.groups)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("groups_term_id_fkey");

            entity.HasOne(d => d.topic).WithMany(p => p.groups)
                .HasForeignKey(d => d.topic_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("groups_topic_id_fkey");
        });

        modelBuilder.Entity<group_member>(entity =>
        {
            entity.HasKey(e => e.id).HasName("group_members_pkey");

            entity.ToTable("group_members", "teammy");

            entity.HasIndex(e => new { e.group_id, e.user_id }, "ux_group_member_unique").IsUnique();

            entity.HasIndex(e => e.group_id, "ux_group_single_leader")
                .IsUnique()
                .HasFilter("(status = 'leader'::text)");

            entity.HasIndex(e => new { e.user_id, e.term_id }, "ux_member_user_term_active")
                .IsUnique()
                .HasFilter("(status = ANY (ARRAY['pending'::text, 'member'::text, 'leader'::text]))");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.joined_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.group).WithOne(p => p.group_member)
                .HasForeignKey<group_member>(d => d.group_id)
                .HasConstraintName("group_members_group_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.group_members)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("group_members_term_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.group_members)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("group_members_user_id_fkey");
        });

        modelBuilder.Entity<invitation>(entity =>
        {
            entity.HasKey(e => e.id).HasName("invitations_pkey");

            entity.ToTable("invitations", "teammy");

            entity.HasIndex(e => e.invitee_user_id, "ix_invitations_invitee");

            entity.HasIndex(e => new { e.group_id, e.invitee_user_id }, "ux_invite_active")
                .IsUnique()
                .HasFilter("(status = 'pending'::text)");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'pending'::text");

            entity.HasOne(d => d.group).WithMany(p => p.invitations)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("invitations_group_id_fkey");

            entity.HasOne(d => d.invited_by_user).WithMany(p => p.invitationinvited_by_users)
                .HasForeignKey(d => d.invited_by_user_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("invitations_invited_by_user_id_fkey");

            entity.HasOne(d => d.invitee_user).WithMany(p => p.invitationinvitee_users)
                .HasForeignKey(d => d.invitee_user_id)
                .HasConstraintName("invitations_invitee_user_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.invitations)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("invitations_term_id_fkey");
        });

        modelBuilder.Entity<label>(entity =>
        {
            entity.HasKey(e => e.id).HasName("labels_pkey");

            entity.ToTable("labels", "teammy");

            entity.HasIndex(e => new { e.group_id, e.name }, "labels_group_id_name_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");

            entity.HasOne(d => d.group).WithMany(p => p.labels)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("labels_group_id_fkey");
        });

        modelBuilder.Entity<major>(entity =>
        {
            entity.HasKey(e => e.id).HasName("majors_pkey");

            entity.ToTable("majors", "teammy");

            entity.HasIndex(e => e.code, "majors_code_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");

            entity.HasOne(d => d.department).WithMany(p => p.majors)
                .HasForeignKey(d => d.department_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("majors_department_id_fkey");
        });

        modelBuilder.Entity<message>(entity =>
        {
            entity.HasKey(e => e.id).HasName("messages_pkey");

            entity.ToTable("messages", "teammy");

            entity.HasIndex(e => new { e.channel_id, e.created_at }, "ix_messages_channel_created").IsDescending(false, true);

            entity.HasIndex(e => new { e.channel_id, e.is_deleted, e.created_at }, "ix_messages_visible").IsDescending(false, false, true);

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.is_deleted).HasDefaultValue(false);
            entity.Property(e => e.meta).HasColumnType("jsonb");

            entity.HasOne(d => d.channel).WithMany(p => p.messages)
                .HasForeignKey(d => d.channel_id)
                .HasConstraintName("messages_channel_id_fkey");

            entity.HasOne(d => d.deleted_byNavigation).WithMany(p => p.messagedeleted_byNavigations)
                .HasForeignKey(d => d.deleted_by)
                .HasConstraintName("messages_deleted_by_fkey");

            entity.HasOne(d => d.sender).WithMany(p => p.messagesenders)
                .HasForeignKey(d => d.sender_id)
                .HasConstraintName("messages_sender_id_fkey");
        });

        modelBuilder.Entity<recruitment_application>(entity =>
        {
            entity.HasKey(e => e.id).HasName("recruitment_applications_pkey");

            entity.ToTable("recruitment_applications", "teammy");

            entity.HasIndex(e => new { e.post_id, e.applicant_user_id }, "ux_applications_unique").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'pending'::text");

            entity.HasOne(d => d.applicant_user).WithMany(p => p.recruitment_applications)
                .HasForeignKey(d => d.applicant_user_id)
                .HasConstraintName("recruitment_applications_applicant_user_id_fkey");

            entity.HasOne(d => d.post).WithMany(p => p.recruitment_applications)
                .HasForeignKey(d => d.post_id)
                .HasConstraintName("recruitment_applications_post_id_fkey");
        });

        modelBuilder.Entity<recruitment_post>(entity =>
        {
            entity.HasKey(e => e.id).HasName("recruitment_posts_pkey");

            entity.ToTable("recruitment_posts", "teammy");

            entity.HasIndex(e => new { e.is_flagged, e.status }, "ix_rp_flag");

            entity.HasIndex(e => e.skills, "ix_rp_skills_gin").HasMethod("gin");

            entity.HasIndex(e => new { e.term_id, e.status, e.post_kind }, "ix_rp_term_status_kind");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.is_flagged).HasDefaultValue(false);
            entity.Property(e => e.skills).HasColumnType("jsonb");
            entity.Property(e => e.status).HasDefaultValueSql("'open'::text");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.recruitment_postcreated_byNavigations)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("recruitment_posts_created_by_fkey");

            entity.HasOne(d => d.group).WithMany(p => p.recruitment_posts)
                .HasForeignKey(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("recruitment_posts_group_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.recruitment_posts)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("recruitment_posts_term_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.recruitment_postusers)
                .HasForeignKey(d => d.user_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("recruitment_posts_user_id_fkey");
        });

        modelBuilder.Entity<role>(entity =>
        {
            entity.HasKey(e => e.id).HasName("roles_pkey");

            entity.ToTable("roles", "teammy");

            entity.HasIndex(e => e.name, "roles_name_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<skill>(entity =>
        {
            entity.HasKey(e => e.id).HasName("skills_pkey");

            entity.ToTable("skills", "teammy");

            entity.HasIndex(e => e.name, "skills_name_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.name).HasColumnType("citext");
        });

        modelBuilder.Entity<student_import_job>(entity =>
        {
            entity.HasKey(e => e.id).HasName("student_import_jobs_pkey");

            entity.ToTable("student_import_jobs", "teammy");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.error_rows).HasDefaultValue(0);
            entity.Property(e => e.errors).HasColumnType("jsonb");
            entity.Property(e => e.status).HasDefaultValueSql("'completed'::text");
            entity.Property(e => e.success_rows).HasDefaultValue(0);
            entity.Property(e => e.total_rows).HasDefaultValue(0);

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.student_import_jobs)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("student_import_jobs_created_by_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.student_import_jobs)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("student_import_jobs_term_id_fkey");
        });

        modelBuilder.Entity<student_profile>(entity =>
        {
            entity.HasKey(e => e.user_id).HasName("student_profiles_pkey");

            entity.ToTable("student_profiles", "teammy");

            entity.HasIndex(e => e.cohort_year, "ix_student_profiles_cohort");

            entity.HasIndex(e => e.major_id, "ix_student_profiles_major");

            entity.HasIndex(e => e.student_code, "student_profiles_student_code_key").IsUnique();

            entity.Property(e => e.user_id).ValueGeneratedNever();
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.skills).HasColumnType("jsonb");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.major).WithMany(p => p.student_profiles)
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("student_profiles_major_id_fkey");

            entity.HasOne(d => d.user).WithOne(p => p.student_profile)
                .HasForeignKey<student_profile>(d => d.user_id)
                .HasConstraintName("student_profiles_user_id_fkey");
        });

        modelBuilder.Entity<task>(entity =>
        {
            entity.HasKey(e => e.id).HasName("tasks_pkey");

            entity.ToTable("tasks", "teammy");

            entity.HasIndex(e => new { e.group_id, e.column_id }, "ix_tasks_group_column");

            entity.HasIndex(e => new { e.group_id, e.created_by }, "ix_tasks_group_createdby");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.column).WithMany(p => p.tasks)
                .HasForeignKey(d => d.column_id)
                .HasConstraintName("tasks_column_id_fkey");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.tasks)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tasks_created_by_fkey");

            entity.HasOne(d => d.group).WithMany(p => p.tasks)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("tasks_group_id_fkey");

            entity.HasMany(d => d.labels).WithMany(p => p.tasks)
                .UsingEntity<Dictionary<string, object>>(
                    "task_label",
                    r => r.HasOne<label>().WithMany()
                        .HasForeignKey("label_id")
                        .HasConstraintName("task_labels_label_id_fkey"),
                    l => l.HasOne<task>().WithMany()
                        .HasForeignKey("task_id")
                        .HasConstraintName("task_labels_task_id_fkey"),
                    j =>
                    {
                        j.HasKey("task_id", "label_id").HasName("task_labels_pkey");
                        j.ToTable("task_labels", "teammy");
                    });

            entity.HasMany(d => d.users).WithMany(p => p.tasksNavigation)
                .UsingEntity<Dictionary<string, object>>(
                    "task_assignee",
                    r => r.HasOne<user>().WithMany()
                        .HasForeignKey("user_id")
                        .HasConstraintName("task_assignees_user_id_fkey"),
                    l => l.HasOne<task>().WithMany()
                        .HasForeignKey("task_id")
                        .HasConstraintName("task_assignees_task_id_fkey"),
                    j =>
                    {
                        j.HasKey("task_id", "user_id").HasName("task_assignees_pkey");
                        j.ToTable("task_assignees", "teammy");
                    });
        });

        modelBuilder.Entity<task_comment>(entity =>
        {
            entity.HasKey(e => e.id).HasName("task_comments_pkey");

            entity.ToTable("task_comments", "teammy");

            entity.HasIndex(e => new { e.task_id, e.parent_comment_id }, "ix_tc_task_parent");

            entity.HasIndex(e => new { e.task_id, e.thread_id, e.created_at }, "ix_tc_task_thread");

            entity.HasIndex(e => new { e.task_id, e.user_id, e.created_at }, "ix_tc_task_user");

            entity.HasIndex(e => new { e.thread_id, e.is_resolved }, "ix_tc_thread_status");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.is_resolved).HasDefaultValue(false);

            entity.HasOne(d => d.parent_comment).WithMany(p => p.Inverseparent_comment)
                .HasForeignKey(d => d.parent_comment_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_comments_parent");

            entity.HasOne(d => d.resolved_byNavigation).WithMany(p => p.task_commentresolved_byNavigations)
                .HasForeignKey(d => d.resolved_by)
                .HasConstraintName("task_comments_resolved_by_fkey");

            entity.HasOne(d => d.task).WithMany(p => p.task_comments)
                .HasForeignKey(d => d.task_id)
                .HasConstraintName("task_comments_task_id_fkey");

            entity.HasOne(d => d.thread).WithMany(p => p.Inversethread)
                .HasForeignKey(d => d.thread_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_comments_thread");

            entity.HasOne(d => d.user).WithMany(p => p.task_commentusers)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("task_comments_user_id_fkey");
        });

        modelBuilder.Entity<team_auto_result>(entity =>
        {
            entity.HasKey(e => new { e.run_id, e.user_id }).HasName("team_auto_results_pkey");

            entity.ToTable("team_auto_results", "teammy");

            entity.Property(e => e.assigned_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.group).WithMany(p => p.team_auto_results)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("team_auto_results_group_id_fkey");

            entity.HasOne(d => d.run).WithMany(p => p.team_auto_results)
                .HasForeignKey(d => d.run_id)
                .HasConstraintName("team_auto_results_run_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.team_auto_results)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("team_auto_results_user_id_fkey");
        });

        modelBuilder.Entity<team_auto_run>(entity =>
        {
            entity.HasKey(e => e.id).HasName("team_auto_runs_pkey");

            entity.ToTable("team_auto_runs", "teammy");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.started_at).HasDefaultValueSql("now()");
            entity.Property(e => e.strategy).HasDefaultValueSql("'skills_random_fallback'::text");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.team_auto_runs)
                .HasForeignKey(d => d.created_by)
                .HasConstraintName("team_auto_runs_created_by_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.team_auto_runs)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("team_auto_runs_term_id_fkey");
        });

        modelBuilder.Entity<team_suggestion>(entity =>
        {
            entity.HasKey(e => new { e.user_id, e.term_id, e.group_id }).HasName("team_suggestions_pkey");

            entity.ToTable("team_suggestions", "teammy");

            entity.HasIndex(e => new { e.user_id, e.term_id, e.score }, "ix_team_sugg_user_score").IsDescending(false, false, true);

            entity.Property(e => e.computed_at).HasDefaultValueSql("now()");
            entity.Property(e => e.reasons).HasColumnType("jsonb");

            entity.HasOne(d => d.group).WithMany(p => p.team_suggestions)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("team_suggestions_group_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.team_suggestions)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("team_suggestions_term_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.team_suggestions)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("team_suggestions_user_id_fkey");
        });

        modelBuilder.Entity<term>(entity =>
        {
            entity.HasKey(e => e.id).HasName("terms_pkey");

            entity.ToTable("terms", "teammy");

            entity.HasIndex(e => e.code, "terms_code_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.is_active).HasDefaultValue(false);
        });

        modelBuilder.Entity<term_import_status>(entity =>
        {
            entity.HasKey(e => e.term_id).HasName("term_import_status_pkey");

            entity.ToTable("term_import_status", "teammy");

            entity.Property(e => e.term_id).ValueGeneratedNever();
            entity.Property(e => e.mentors_ready).HasDefaultValue(false);
            entity.Property(e => e.topics_ready).HasDefaultValue(false);
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.term).WithOne(p => p.term_import_status)
                .HasForeignKey<term_import_status>(d => d.term_id)
                .HasConstraintName("term_import_status_term_id_fkey");
        });

        modelBuilder.Entity<term_policy>(entity =>
        {
            entity.HasKey(e => e.term_id).HasName("term_policies_pkey");

            entity.ToTable("term_policies", "teammy");

            entity.Property(e => e.term_id).ValueGeneratedNever();
            entity.Property(e => e.desired_group_size_max).HasDefaultValue(5);
            entity.Property(e => e.desired_group_size_min).HasDefaultValue(3);

            entity.HasOne(d => d.term).WithOne(p => p.term_policy)
                .HasForeignKey<term_policy>(d => d.term_id)
                .HasConstraintName("term_policies_term_id_fkey");
        });

        modelBuilder.Entity<topic>(entity =>
        {
            entity.HasKey(e => e.id).HasName("topics_pkey");

            entity.ToTable("topics", "teammy");

            entity.HasIndex(e => e.term_id, "ix_topics_term");

            entity.HasIndex(e => new { e.term_id, e.title }, "topics_term_id_title_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'open'::text");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.topics)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("topics_created_by_fkey");

            entity.HasOne(d => d.department).WithMany(p => p.topics)
                .HasForeignKey(d => d.department_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("topics_department_id_fkey");

            entity.HasOne(d => d.major).WithMany(p => p.topics)
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("topics_major_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.topics)
                .HasForeignKey(d => d.term_id)
                .HasConstraintName("topics_term_id_fkey");
        });

        modelBuilder.Entity<topic_import_job>(entity =>
        {
            entity.HasKey(e => e.id).HasName("topic_import_jobs_pkey");

            entity.ToTable("topic_import_jobs", "teammy");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.error_rows).HasDefaultValue(0);
            entity.Property(e => e.errors).HasColumnType("jsonb");
            entity.Property(e => e.status).HasDefaultValueSql("'completed'::text");
            entity.Property(e => e.success_rows).HasDefaultValue(0);
            entity.Property(e => e.total_rows).HasDefaultValue(0);

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.topic_import_jobs)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("topic_import_jobs_created_by_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.topic_import_jobs)
                .HasForeignKey(d => d.term_id)
                .HasConstraintName("topic_import_jobs_term_id_fkey");
        });

        modelBuilder.Entity<topic_mentor>(entity =>
        {
            entity.HasKey(e => new { e.topic_id, e.mentor_id }).HasName("topic_mentors_pkey");

            entity.ToTable("topic_mentors", "teammy");

            entity.HasIndex(e => e.mentor_id, "ix_topic_mentors_mentor");

            entity.Property(e => e.role_on_topic).HasDefaultValueSql("'owner'::text");

            entity.HasOne(d => d.mentor).WithMany(p => p.topic_mentors)
                .HasForeignKey(d => d.mentor_id)
                .HasConstraintName("topic_mentors_mentor_id_fkey");

            entity.HasOne(d => d.topic).WithMany(p => p.topic_mentors)
                .HasForeignKey(d => d.topic_id)
                .HasConstraintName("topic_mentors_topic_id_fkey");
        });

        modelBuilder.Entity<topic_suggestion>(entity =>
        {
            entity.HasKey(e => new { e.subject_type, e.subject_id, e.topic_id }).HasName("topic_suggestions_pkey");

            entity.ToTable("topic_suggestions", "teammy");

            entity.HasIndex(e => new { e.subject_type, e.subject_id, e.score }, "ix_topic_sugg_subject_score").IsDescending(false, false, true);

            entity.Property(e => e.computed_at).HasDefaultValueSql("now()");
            entity.Property(e => e.reasons).HasColumnType("jsonb");

            entity.HasOne(d => d.term).WithMany(p => p.topic_suggestions)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("topic_suggestions_term_id_fkey");

            entity.HasOne(d => d.topic).WithMany(p => p.topic_suggestions)
                .HasForeignKey(d => d.topic_id)
                .HasConstraintName("topic_suggestions_topic_id_fkey");
        });

        modelBuilder.Entity<user>(entity =>
        {
            entity.HasKey(e => e.id).HasName("users_pkey");

            entity.ToTable("users", "teammy");

            entity.HasIndex(e => e.email, "users_email_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.email).HasColumnType("citext");
            entity.Property(e => e.email_verified).HasDefaultValue(false);
            entity.Property(e => e.is_active).HasDefaultValue(true);

            entity.HasOne(d => d.role).WithMany(p => p.users)
                .HasForeignKey(d => d.role_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("users_role_id_fkey");

            entity.HasMany(d => d.skills).WithMany(p => p.users)
                .UsingEntity<Dictionary<string, object>>(
                    "user_skill",
                    r => r.HasOne<skill>().WithMany()
                        .HasForeignKey("skill_id")
                        .HasConstraintName("user_skills_skill_id_fkey"),
                    l => l.HasOne<user>().WithMany()
                        .HasForeignKey("user_id")
                        .HasConstraintName("user_skills_user_id_fkey"),
                    j =>
                    {
                        j.HasKey("user_id", "skill_id").HasName("user_skills_pkey");
                        j.ToTable("user_skills", "teammy");
                    });
        });

        modelBuilder.Entity<user_report>(entity =>
        {
            entity.HasKey(e => e.id).HasName("user_reports_pkey");

            entity.ToTable("user_reports", "teammy");

            entity.HasIndex(e => new { e.status, e.created_at }, "ix_reports_status").IsDescending(false, true);

            entity.HasIndex(e => new { e.target_type, e.target_id }, "ix_reports_target");

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'open'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.assigned_toNavigation).WithMany(p => p.user_reportassigned_toNavigations)
                .HasForeignKey(d => d.assigned_to)
                .HasConstraintName("user_reports_assigned_to_fkey");

            entity.HasOne(d => d.reporter).WithMany(p => p.user_reportreporters)
                .HasForeignKey(d => d.reporter_id)
                .HasConstraintName("user_reports_reporter_id_fkey");

            entity.HasOne(d => d.term).WithMany(p => p.user_reports)
                .HasForeignKey(d => d.term_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("user_reports_term_id_fkey");
        });

        modelBuilder.Entity<vw_admin_overview>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_admin_overview", "teammy");
        });

        modelBuilder.Entity<vw_groups_without_topic>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_groups_without_topic", "teammy");
        });

        modelBuilder.Entity<vw_students_without_group>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_students_without_group", "teammy");

            entity.Property(e => e.skills).HasColumnType("jsonb");
        });

        modelBuilder.Entity<vw_term_phase>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_term_phase", "teammy");
        });

        modelBuilder.Entity<vw_topics_available>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_topics_available", "teammy");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
