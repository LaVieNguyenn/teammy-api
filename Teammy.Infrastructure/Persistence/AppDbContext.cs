using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<activity_log> activity_logs { get; set; }
    public virtual DbSet<announcement> announcements { get; set; }

    public virtual DbSet<backlog_item> backlog_items { get; set; }

    public virtual DbSet<board> boards { get; set; }

    public virtual DbSet<candidate> candidates { get; set; }

    public virtual DbSet<chat_session> chat_sessions { get; set; }

    public virtual DbSet<chat_session_participant> chat_session_participants { get; set; }

    public virtual DbSet<column> columns { get; set; }

    public virtual DbSet<comment> comments { get; set; }

    public virtual DbSet<group> groups { get; set; }

    public virtual DbSet<group_member> group_members { get; set; }
    public virtual DbSet<group_member_role> group_member_roles { get; set; }

    public virtual DbSet<invitation> invitations { get; set; }

    public virtual DbSet<major> majors { get; set; }

    public virtual DbSet<message> messages { get; set; }

    public virtual DbSet<milestone> milestones { get; set; }

    public virtual DbSet<milestone_item> milestone_items { get; set; }

    public virtual DbSet<mv_group_capacity> mv_group_capacities { get; set; }

    public virtual DbSet<mv_group_topic_match> mv_group_topic_matches { get; set; }

    public virtual DbSet<mv_students_pool> mv_students_pools { get; set; }

    public virtual DbSet<recruitment_post> recruitment_posts { get; set; }

    public virtual DbSet<role> roles { get; set; }

    public virtual DbSet<semester> semesters { get; set; }

    public virtual DbSet<semester_policy> semester_policies { get; set; }

    public virtual DbSet<shared_file> shared_files { get; set; }

    public virtual DbSet<skill_alias> skill_aliases { get; set; }

    public virtual DbSet<skill_dictionary> skill_dictionaries { get; set; }

    public virtual DbSet<task> tasks { get; set; }

    public virtual DbSet<task_assignment> task_assignments { get; set; }

    public virtual DbSet<topic> topics { get; set; }

    public virtual DbSet<user> users { get; set; }

    public virtual DbSet<user_report> user_reports { get; set; }

    public virtual DbSet<user_role> user_roles { get; set; }

    public virtual DbSet<vw_groups_without_topic> vw_groups_without_topics { get; set; }

    public virtual DbSet<vw_topics_available> vw_topics_availables { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("teammy", "season_enum", new[] { "SPRING", "SUMMER", "FALL" })
            .HasPostgresExtension("citext")
            .HasPostgresExtension("pgcrypto")
            .HasPostgresExtension("unaccent");

        modelBuilder.Entity<activity_log>(entity =>
        {
            entity.HasKey(e => e.activity_id).HasName("activity_logs_pkey");

            entity.ToTable("activity_logs", "teammy");

            entity.HasIndex(e => new { e.group_id, e.created_at }, "ix_activity_logs_group").IsDescending(false, true);
            entity.HasIndex(e => new { e.actor_id, e.created_at }, "ix_activity_logs_actor").IsDescending(false, true);

            entity.Property(e => e.activity_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValue("success");
            entity.Property(e => e.severity).HasDefaultValue("info");
            entity.Property(e => e.metadata).HasColumnType("jsonb");

            entity.HasOne(d => d.actor).WithMany()
                .HasForeignKey(d => d.actor_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("activity_logs_actor_id_fkey");

            entity.HasOne(d => d.target_user).WithMany()
                .HasForeignKey(d => d.target_user_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("activity_logs_target_user_id_fkey");

            entity.HasOne(d => d.group).WithMany()
                .HasForeignKey(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("activity_logs_group_id_fkey");
        });

        modelBuilder.Entity<announcement>(entity =>
        {
            entity.HasKey(e => e.announcement_id).HasName("announcements_pkey");

            entity.ToTable("announcements", "teammy");

            entity.Property(e => e.announcement_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.pinned).HasDefaultValue(false);
            entity.Property(e => e.publish_at).HasDefaultValueSql("now()");
            entity.Property(e => e.scope).HasDefaultValueSql("'semester'::text");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.announcements)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("announcements_created_by_fkey");

            entity.HasOne(d => d.semester).WithMany(p => p.announcements)
                .HasForeignKey(d => d.semester_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("announcements_semester_id_fkey");

            entity.HasOne(d => d.target_group).WithMany(p => p.announcements)
                .HasForeignKey(d => d.target_group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("announcements_target_group_id_fkey");
        });

        modelBuilder.Entity<backlog_item>(entity =>
        {
            entity.HasKey(e => e.backlog_item_id).HasName("backlog_items_pkey");

            entity.ToTable("backlog_items", "teammy");

            entity.HasIndex(e => e.owner_user_id, "ix_backlog_items_owner");

            entity.HasIndex(e => new { e.group_id, e.status }, "ix_backlog_items_group_status");

            entity.Property(e => e.backlog_item_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'planned'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.backlog_itemcreated_bies)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("backlog_items_created_by_fkey");

            entity.HasOne(d => d.group).WithMany(p => p.backlog_items)
                .HasForeignKey(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("backlog_items_group_id_fkey");

            entity.HasOne(d => d.owner_user).WithMany(p => p.backlog_itemowner_users)
                .HasForeignKey(d => d.owner_user_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("backlog_items_owner_user_id_fkey");
        });

        modelBuilder.Entity<board>(entity =>
        {
            entity.HasKey(e => e.board_id).HasName("boards_pkey");

            entity.ToTable("boards", "teammy");

            entity.HasIndex(e => e.group_id, "boards_group_id_key").IsUnique();

            entity.Property(e => e.board_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.board_name).HasDefaultValueSql("'Board'::text");

            entity.HasOne(d => d.group).WithOne(p => p.board)
                .HasForeignKey<board>(d => d.group_id)
                .HasConstraintName("boards_group_id_fkey");
        });

        modelBuilder.Entity<candidate>(entity =>
        {
            entity.HasKey(e => e.candidate_id).HasName("candidates_pkey");

            entity.ToTable("candidates", "teammy");

            entity.HasIndex(e => new { e.post_id, e.applicant_group_id }, "ux_cand_post_group")
                .IsUnique()
                .HasFilter("(applicant_group_id IS NOT NULL)");

            entity.HasIndex(e => new { e.post_id, e.applicant_user_id }, "ux_cand_post_user")
                .IsUnique()
                .HasFilter("(applicant_user_id IS NOT NULL)");

            entity.Property(e => e.candidate_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'pending'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.applicant_group).WithMany(p => p.candidates)
                .HasForeignKey(d => d.applicant_group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("candidates_applicant_group_id_fkey");

            entity.HasOne(d => d.applicant_user).WithMany(p => p.candidateapplicant_users)
                .HasForeignKey(d => d.applicant_user_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("candidates_applicant_user_id_fkey");

            entity.HasOne(d => d.applied_by_user).WithMany(p => p.candidateapplied_by_users)
                .HasForeignKey(d => d.applied_by_user_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("candidates_applied_by_user_id_fkey");

            entity.HasOne(d => d.post).WithMany(p => p.candidates)
                .HasForeignKey(d => d.post_id)
                .HasConstraintName("candidates_post_id_fkey");
        });

        modelBuilder.Entity<chat_session>(entity =>
        {
            entity.HasKey(e => e.chat_session_id).HasName("chat_sessions_pkey");

            entity.ToTable("chat_sessions", "teammy");

            entity.HasIndex(e => e.group_id, "ux_chat_project_single")
                .IsUnique()
                .HasFilter("(type = 'project'::text)");

            entity.Property(e => e.chat_session_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.members).HasDefaultValue(0);
            entity.Property(e => e.type).HasDefaultValueSql("'group'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.group).WithOne(p => p.chat_session)
                .HasForeignKey<chat_session>(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("chat_sessions_group_id_fkey");
        });

        modelBuilder.Entity<chat_session_participant>(entity =>
        {
            entity.HasKey(e => new { e.chat_session_id, e.user_id }).HasName("chat_session_participants_pkey");

            entity.ToTable("chat_session_participants", "teammy");

            entity.HasIndex(e => e.user_id, "ix_chat_session_participants_user");

            entity.Property(e => e.joined_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.chat_session).WithMany(p => p.chat_session_participants)
                .HasForeignKey(d => d.chat_session_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("chat_session_participants_session_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.chat_session_participants)
                .HasForeignKey(d => d.user_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("chat_session_participants_user_id_fkey");
        });

        modelBuilder.Entity<column>(entity =>
        {
            entity.HasKey(e => e.column_id).HasName("columns_pkey");

            entity.ToTable("columns", "teammy");

            entity.HasIndex(e => new { e.board_id, e.column_name }, "columns_board_id_column_name_key").IsUnique();

            entity.HasIndex(e => new { e.board_id, e.position }, "columns_board_id_position_key").IsUnique();

            entity.Property(e => e.column_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.is_done).HasDefaultValue(false);
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.board).WithMany(p => p.columns)
                .HasForeignKey(d => d.board_id)
                .HasConstraintName("columns_board_id_fkey");
        });

        modelBuilder.Entity<comment>(entity =>
        {
            entity.HasKey(e => e.comment_id).HasName("comments_pkey");

            entity.ToTable("comments", "teammy");

            entity.Property(e => e.comment_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.task).WithMany(p => p.comments)
                .HasForeignKey(d => d.task_id)
                .HasConstraintName("comments_task_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.comments)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("comments_user_id_fkey");
        });

        modelBuilder.Entity<group>(entity =>
        {
            entity.HasKey(e => e.group_id).HasName("groups_pkey");

            entity.ToTable("groups", "teammy");

            entity.HasIndex(e => new { e.semester_id, e.name }, "groups_semester_id_name_key").IsUnique();

            entity.HasIndex(e => e.topic_id, "ux_group_unique_topic")
                .IsUnique()
                .HasFilter("(topic_id IS NOT NULL)");

            entity.Property(e => e.group_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'recruiting'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");
            entity.Property(e => e.skills).HasColumnType("jsonb");

            entity.HasOne(d => d.major).WithMany(p => p.groups)
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("groups_major_id_fkey");

            entity.HasOne(d => d.mentor).WithMany(p => p.groups)
                .HasForeignKey(d => d.mentor_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("groups_mentor_id_fkey");

            entity.HasOne(d => d.semester).WithMany(p => p.groups)
                .HasForeignKey(d => d.semester_id)
                .HasConstraintName("groups_semester_id_fkey");

            entity.HasOne(d => d.topic).WithOne(p => p.group)
                .HasForeignKey<group>(d => d.topic_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("groups_topic_id_fkey");
        });

        modelBuilder.Entity<group_member>(entity =>
        {
            entity.HasKey(e => e.group_member_id).HasName("group_members_pkey");

            entity.ToTable("group_members", "teammy");

            entity.HasIndex(e => e.group_id, "ux_group_single_leader")
                .IsUnique()
                .HasFilter("(status = 'leader'::text)");

            entity.HasIndex(e => new { e.user_id, e.semester_id }, "ux_member_user_semester_active")
                .IsUnique()
                .HasFilter("(status = ANY (ARRAY['pending'::text, 'member'::text, 'leader'::text]))");

            entity.Property(e => e.group_member_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.joined_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.group).WithMany(p => p.group_members)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("group_members_group_id_fkey");

            entity.HasOne(d => d.semester).WithMany(p => p.group_members)
                .HasForeignKey(d => d.semester_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("group_members_semester_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.group_members)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("group_members_user_id_fkey");
        });

        modelBuilder.Entity<group_member_role>(entity =>
        {
            entity.HasKey(e => e.group_member_role_id).HasName("group_member_roles_pkey");

            entity.ToTable("group_member_roles", "teammy");

            entity.HasIndex(e => new { e.group_member_id, e.role_name }, "ux_group_member_role")
                .IsUnique();

            entity.Property(e => e.group_member_role_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.assigned_at).HasDefaultValueSql("now()");

            entity.HasOne<group_member>()
                .WithMany()
                .HasForeignKey(e => e.group_member_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("group_member_roles_group_member_id_fkey");
        });

        modelBuilder.Entity<invitation>(entity =>
        {
            entity.HasKey(e => e.invitation_id).HasName("invitations_pkey");

            entity.ToTable("invitations", "teammy");

            entity.Property(e => e.invitation_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'pending'::text");

            entity.HasOne(d => d.group).WithMany(p => p.invitations)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("fk_invitations_group");

            entity.HasOne(d => d.topic).WithMany(p => p.invitations)
                .HasForeignKey(d => d.topic_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("invitations_topic_id_fkey");

            entity.HasOne(d => d.invited_byNavigation).WithMany(p => p.invitationinvited_byNavigations)
                .HasForeignKey(d => d.invited_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("invitations_invited_by_fkey");

            entity.HasOne(d => d.invitee_user).WithMany(p => p.invitationinvitee_users)
                .HasForeignKey(d => d.invitee_user_id)
                .HasConstraintName("invitations_invitee_user_id_fkey");
        });

        modelBuilder.Entity<major>(entity =>
        {
            entity.HasKey(e => e.major_id).HasName("majors_pkey");

            entity.ToTable("majors", "teammy");

            entity.HasIndex(e => e.major_name, "majors_major_name_key").IsUnique();

            entity.Property(e => e.major_id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<message>(entity =>
        {
            entity.HasKey(e => e.message_id).HasName("messages_pkey");

            entity.ToTable("messages", "teammy");

            entity.HasIndex(e => new { e.chat_session_id, e.created_at }, "ix_messages_session_created").IsDescending(false, true);

            entity.Property(e => e.message_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.chat_session).WithMany(p => p.messages)
                .HasForeignKey(d => d.chat_session_id)
                .HasConstraintName("messages_chat_session_id_fkey");

            entity.HasOne(d => d.sender).WithMany(p => p.messages)
                .HasForeignKey(d => d.sender_id)
                .HasConstraintName("messages_sender_id_fkey");
        });

        modelBuilder.Entity<milestone>(entity =>
        {
            entity.HasKey(e => e.milestone_id).HasName("milestones_pkey");

            entity.ToTable("milestones", "teammy");

            entity.HasIndex(e => e.target_date, "ix_milestones_target_date");

            entity.HasIndex(e => new { e.group_id, e.status }, "ix_milestones_group_status");

            entity.Property(e => e.milestone_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'planned'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.milestone_created_bies)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("milestones_created_by_fkey");

            entity.HasOne(d => d.group).WithMany(p => p.milestones)
                .HasForeignKey(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("milestones_group_id_fkey");
        });

        modelBuilder.Entity<milestone_item>(entity =>
        {
            entity.HasKey(e => e.milestone_item_id).HasName("milestone_items_pkey");

            entity.ToTable("milestone_items", "teammy");

            entity.HasIndex(e => e.backlog_item_id, "ux_milestone_items_backlog").IsUnique();

            entity.HasIndex(e => new { e.milestone_id, e.backlog_item_id }, "milestone_items_milestone_id_backlog_item_id_key").IsUnique();

            entity.Property(e => e.milestone_item_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.added_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.backlog_item).WithMany(p => p.milestone_items)
                .HasForeignKey(d => d.backlog_item_id)
                .HasConstraintName("milestone_items_backlog_item_id_fkey");

            entity.HasOne(d => d.milestone).WithMany(p => p.milestone_items)
                .HasForeignKey(d => d.milestone_id)
                .HasConstraintName("milestone_items_milestone_id_fkey");
        });

        modelBuilder.Entity<mv_group_capacity>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("mv_group_capacity", "teammy");
        });

        modelBuilder.Entity<mv_group_topic_match>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("mv_group_topic_match", "teammy");
        });

        modelBuilder.Entity<mv_students_pool>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("mv_students_pool", "teammy");

            entity.Property(e => e.skills).HasColumnType("jsonb");
        });

        modelBuilder.Entity<recruitment_post>(entity =>
        {
            entity.HasKey(e => e.post_id).HasName("recruitment_posts_pkey");

            entity.ToTable("recruitment_posts", "teammy");

            entity.HasIndex(e => new { e.semester_id, e.status, e.post_type }, "ix_posts_semester_status_type");

            entity.HasIndex(e => e.required_skills, "ix_rp_required_skills_gin")
                .HasMethod("gin")
                .HasOperators(new[] { "jsonb_path_ops" });

            entity.Property(e => e.post_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.required_skills).HasColumnType("jsonb");
            entity.Property(e => e.status).HasDefaultValueSql("'open'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.group).WithMany(p => p.recruitment_posts)
                .HasForeignKey(d => d.group_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("recruitment_posts_group_id_fkey");

            entity.HasOne(d => d.major).WithMany(p => p.recruitment_posts)
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("recruitment_posts_major_id_fkey");

            entity.HasOne(d => d.semester).WithMany(p => p.recruitment_posts)
                .HasForeignKey(d => d.semester_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("recruitment_posts_semester_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.recruitment_posts)
                .HasForeignKey(d => d.user_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("recruitment_posts_user_id_fkey");
        });

        modelBuilder.Entity<role>(entity =>
        {
            entity.HasKey(e => e.role_id).HasName("roles_pkey");

            entity.ToTable("roles", "teammy");

            entity.HasIndex(e => e.name, "roles_name_key").IsUnique();

            entity.Property(e => e.role_id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<semester>(entity =>
        {
            entity.HasKey(e => e.semester_id).HasName("semesters_pkey");

            entity.ToTable("semesters", "teammy");

            entity.HasIndex(e => new { e.season, e.year }, "uq_semesters_season_year").IsUnique();

            entity.Property(e => e.semester_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.is_active).HasDefaultValue(false);
        });

        modelBuilder.Entity<semester_policy>(entity =>
        {
            entity.HasKey(e => e.semester_id).HasName("semester_policy_pkey");

            entity.ToTable("semester_policy", "teammy");

            entity.Property(e => e.semester_id).ValueGeneratedNever();
            entity.Property(e => e.desired_group_size_max).HasDefaultValue(6);
            entity.Property(e => e.desired_group_size_min).HasDefaultValue(4);

            entity.HasOne(d => d.semester).WithOne(p => p.semester_policy)
                .HasForeignKey<semester_policy>(d => d.semester_id)
                .HasConstraintName("semester_policy_semester_id_fkey");
        });

        modelBuilder.Entity<shared_file>(entity =>
        {
            entity.HasKey(e => e.file_id).HasName("shared_files_pkey");

            entity.ToTable("shared_files", "teammy");

            entity.Property(e => e.file_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");
            entity.Property(e => e.file_name).HasMaxLength(255);

            entity.HasOne(d => d.group).WithMany(p => p.shared_files)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("shared_files_group_id_fkey");

            entity.HasOne(d => d.task).WithMany(p => p.shared_files)
                .HasForeignKey(d => d.task_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("shared_files_task_id_fkey");

            entity.HasOne(d => d.uploaded_byNavigation).WithMany(p => p.shared_files)
                .HasForeignKey(d => d.uploaded_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("shared_files_uploaded_by_fkey");
        });

        modelBuilder.Entity<skill_alias>(entity =>
        {
            entity.HasKey(e => e.alias).HasName("skill_aliases_pkey");

            entity.ToTable("skill_aliases", "teammy");

            entity.Property(e => e.alias).HasColumnType("citext");
            entity.Property(e => e.token).HasColumnType("citext");

            entity.HasOne(d => d.tokenNavigation).WithMany(p => p.skill_aliases)
                .HasForeignKey(d => d.token)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("skill_aliases_token_fkey");
        });

        modelBuilder.Entity<skill_dictionary>(entity =>
        {
            entity.HasKey(e => e.token).HasName("skill_dictionary_pkey");

            entity.ToTable("skill_dictionary", "teammy");

            entity.Property(e => e.token).HasColumnType("citext");
        });

        modelBuilder.Entity<task>(entity =>
        {
            entity.HasKey(e => e.task_id).HasName("tasks_pkey");

            entity.ToTable("tasks", "teammy");

            entity.HasIndex(e => new { e.column_id, e.sort_order }, "idx_tasks_column_sort");

            entity.HasIndex(e => e.backlog_item_id, "ux_tasks_backlog_item")
                .IsUnique()
                .HasFilter("(backlog_item_id IS NOT NULL)");

            entity.Property(e => e.task_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.sort_order).HasPrecision(20, 6);
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.column).WithMany(p => p.tasks)
                .HasForeignKey(d => d.column_id)
                .HasConstraintName("tasks_column_id_fkey");

            entity.HasOne(d => d.group).WithMany(p => p.tasks)
                .HasForeignKey(d => d.group_id)
                .HasConstraintName("tasks_group_id_fkey");

            entity.HasOne(d => d.backlog_item).WithMany(p => p.tasks)
                .HasForeignKey(d => d.backlog_item_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("tasks_backlog_item_id_fkey");
        });

        modelBuilder.Entity<task_assignment>(entity =>
        {
            entity.HasKey(e => e.task_assignment_id).HasName("task_assignments_pkey");

            entity.ToTable("task_assignments", "teammy");

            entity.HasIndex(e => new { e.task_id, e.user_id }, "task_assignments_task_id_user_id_key").IsUnique();

            entity.Property(e => e.task_assignment_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.assigned_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.task).WithMany(p => p.task_assignments)
                .HasForeignKey(d => d.task_id)
                .HasConstraintName("task_assignments_task_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.task_assignments)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("task_assignments_user_id_fkey");
        });

        modelBuilder.Entity<topic>(entity =>
        {
            entity.HasKey(e => e.topic_id).HasName("topics_pkey");

            entity.ToTable("topics", "teammy");

            entity.HasIndex(e => new { e.semester_id, e.title }, "topics_semester_id_title_key").IsUnique();

            entity.Property(e => e.topic_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'open'::text");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.topics)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("topics_created_by_fkey");

            entity.HasOne(d => d.major).WithMany(p => p.topics)
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("topics_major_id_fkey");

            entity.HasOne(d => d.semester).WithMany(p => p.topics)
                .HasForeignKey(d => d.semester_id)
                .HasConstraintName("topics_semester_id_fkey");

            entity.HasMany(d => d.mentors).WithMany(p => p.topicsNavigation)
                .UsingEntity<Dictionary<string, object>>(
                    "topics_mentor",
                    r => r.HasOne<user>().WithMany()
                        .HasForeignKey("mentor_id")
                        .HasConstraintName("topics_mentor_mentor_id_fkey"),
                    l => l.HasOne<topic>().WithMany()
                        .HasForeignKey("topic_id")
                        .HasConstraintName("topics_mentor_topic_id_fkey"),
                    j =>
                    {
                        j.HasKey("topic_id", "mentor_id").HasName("topics_mentor_pkey");
                        j.ToTable("topics_mentor", "teammy");
                    });
        });

        modelBuilder.Entity<user>(entity =>
        {
            entity.HasKey(e => e.user_id).HasName("users_pkey");

            entity.ToTable("users", "teammy");

            entity.HasIndex(e => e.email, "users_email_key").IsUnique();

            entity.Property(e => e.user_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.email).HasColumnType("citext");
            entity.Property(e => e.email_verified).HasDefaultValue(false);
            entity.Property(e => e.is_active).HasDefaultValue(true);
            entity.Property(e => e.skills).HasColumnType("jsonb");
            entity.Property(e => e.skills_completed).HasDefaultValue(false);
            entity.Property(e => e.student_code).HasMaxLength(30);
            entity.Property(e => e.portfolio_url).HasColumnType("text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.major).WithMany(p => p.users)
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("users_major_id_fkey");
        });

        modelBuilder.Entity<user_report>(entity =>
        {
            entity.HasKey(e => e.report_id).HasName("user_reports_pkey");

            entity.ToTable("user_reports", "teammy");

            entity.HasIndex(e => new { e.status, e.created_at }, "ix_reports_status").IsDescending(false, true);

            entity.HasIndex(e => new { e.target_type, e.target_id }, "ix_reports_target");

            entity.Property(e => e.report_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");
            entity.Property(e => e.status).HasDefaultValueSql("'open'::text");
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.assigned_toNavigation).WithMany(p => p.user_reportassigned_toNavigations)
                .HasForeignKey(d => d.assigned_to)
                .HasConstraintName("user_reports_assigned_to_fkey");

            entity.HasOne(d => d.reporter).WithMany(p => p.user_reportreporters)
                .HasForeignKey(d => d.reporter_id)
                .HasConstraintName("user_reports_reporter_id_fkey");

            entity.HasOne(d => d.semester).WithMany(p => p.user_reports)
                .HasForeignKey(d => d.semester_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("user_reports_semester_id_fkey");
        });

        modelBuilder.Entity<user_role>(entity =>
        {
            entity.HasKey(e => e.user_role_id).HasName("user_roles_pkey");

            entity.ToTable("user_roles", "teammy");

            entity.HasIndex(e => new { e.user_id, e.role_id }, "user_roles_user_id_role_id_key").IsUnique();

            entity.Property(e => e.user_role_id).HasDefaultValueSql("gen_random_uuid()");

            entity.HasOne(d => d.role).WithMany(p => p.user_roles)
                .HasForeignKey(d => d.role_id)
                .HasConstraintName("user_roles_role_id_fkey");

            entity.HasOne(d => d.user).WithMany(p => p.user_roles)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("user_roles_user_id_fkey");
        });

        modelBuilder.Entity<vw_groups_without_topic>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_groups_without_topic", "teammy");
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
