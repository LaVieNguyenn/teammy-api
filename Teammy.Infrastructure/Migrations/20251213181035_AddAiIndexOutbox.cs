using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Teammy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiIndexOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "teammy");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:teammy.season_enum", "SPRING,SUMMER,FALL")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "ai_index_outbox",
                schema: "teammy",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    major_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_index_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "majors",
                schema: "teammy",
                columns: table => new
                {
                    major_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    major_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("majors_pkey", x => x.major_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "teammy",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("roles_pkey", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "semesters",
                schema: "teammy",
                columns: table => new
                {
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    season = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("semesters_pkey", x => x.semester_id);
                });

            migrationBuilder.CreateTable(
                name: "skill_dictionary",
                schema: "teammy",
                columns: table => new
                {
                    token = table.Column<string>(type: "citext", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    major = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("skill_dictionary_pkey", x => x.token);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "teammy",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "citext", nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    student_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    major_id = table.Column<Guid>(type: "uuid", nullable: true),
                    portfolio_url = table.Column<string>(type: "text", nullable: true),
                    skills = table.Column<string>(type: "jsonb", nullable: true),
                    skills_completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("users_pkey", x => x.user_id);
                    table.ForeignKey(
                        name: "users_major_id_fkey",
                        column: x => x.major_id,
                        principalSchema: "teammy",
                        principalTable: "majors",
                        principalColumn: "major_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "semester_policy",
                schema: "teammy",
                columns: table => new
                {
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_self_select_start = table.Column<DateOnly>(type: "date", nullable: false),
                    team_self_select_end = table.Column<DateOnly>(type: "date", nullable: false),
                    team_suggest_start = table.Column<DateOnly>(type: "date", nullable: false),
                    topic_self_select_start = table.Column<DateOnly>(type: "date", nullable: false),
                    topic_self_select_end = table.Column<DateOnly>(type: "date", nullable: false),
                    topic_suggest_start = table.Column<DateOnly>(type: "date", nullable: false),
                    desired_group_size_min = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    desired_group_size_max = table.Column<int>(type: "integer", nullable: false, defaultValue: 6)
                },
                constraints: table =>
                {
                    table.PrimaryKey("semester_policy_pkey", x => x.semester_id);
                    table.ForeignKey(
                        name: "semester_policy_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "skill_aliases",
                schema: "teammy",
                columns: table => new
                {
                    alias = table.Column<string>(type: "citext", nullable: false),
                    token = table.Column<string>(type: "citext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("skill_aliases_pkey", x => x.alias);
                    table.ForeignKey(
                        name: "skill_aliases_token_fkey",
                        column: x => x.token,
                        principalSchema: "teammy",
                        principalTable: "skill_dictionary",
                        principalColumn: "token");
                });

            migrationBuilder.CreateTable(
                name: "topics",
                schema: "teammy",
                columns: table => new
                {
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    major_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: true),
                    skills = table.Column<string>(type: "jsonb", nullable: true),
                    source_file_name = table.Column<string>(type: "text", nullable: true),
                    source_file_type = table.Column<string>(type: "text", nullable: true),
                    source_file_size = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'open'::text"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("topics_pkey", x => x.topic_id);
                    table.ForeignKey(
                        name: "topics_created_by_fkey",
                        column: x => x.created_by,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "topics_major_id_fkey",
                        column: x => x.major_id,
                        principalSchema: "teammy",
                        principalTable: "majors",
                        principalColumn: "major_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "topics_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_reports",
                schema: "teammy",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'open'::text"),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_reports_pkey", x => x.report_id);
                    table.ForeignKey(
                        name: "user_reports_assigned_to_fkey",
                        column: x => x.assigned_to,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "user_reports_reporter_id_fkey",
                        column: x => x.reporter_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "user_reports_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "teammy",
                columns: table => new
                {
                    user_role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_roles_pkey", x => x.user_role_id);
                    table.ForeignKey(
                        name: "user_roles_role_id_fkey",
                        column: x => x.role_id,
                        principalSchema: "teammy",
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "user_roles_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                schema: "teammy",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mentor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    major_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    max_members = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'recruiting'::text"),
                    skills = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("groups_pkey", x => x.group_id);
                    table.ForeignKey(
                        name: "groups_major_id_fkey",
                        column: x => x.major_id,
                        principalSchema: "teammy",
                        principalTable: "majors",
                        principalColumn: "major_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "groups_mentor_id_fkey",
                        column: x => x.mentor_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "groups_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "groups_topic_id_fkey",
                        column: x => x.topic_id,
                        principalSchema: "teammy",
                        principalTable: "topics",
                        principalColumn: "topic_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "topics_mentor",
                schema: "teammy",
                columns: table => new
                {
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentor_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("topics_mentor_pkey", x => new { x.topic_id, x.mentor_id });
                    table.ForeignKey(
                        name: "topics_mentor_mentor_id_fkey",
                        column: x => x.mentor_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "topics_mentor_topic_id_fkey",
                        column: x => x.topic_id,
                        principalSchema: "teammy",
                        principalTable: "topics",
                        principalColumn: "topic_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_logs",
                schema: "teammy",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "success"),
                    platform = table.Column<string>(type: "text", nullable: true),
                    severity = table.Column<string>(type: "text", nullable: false, defaultValue: "info"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("activity_logs_pkey", x => x.activity_id);
                    table.ForeignKey(
                        name: "activity_logs_actor_id_fkey",
                        column: x => x.actor_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "activity_logs_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "activity_logs_target_user_id_fkey",
                        column: x => x.target_user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "announcements",
                schema: "teammy",
                columns: table => new
                {
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'semester'::text"),
                    target_role = table.Column<string>(type: "text", nullable: true),
                    target_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    publish_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expire_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("announcements_pkey", x => x.announcement_id);
                    table.ForeignKey(
                        name: "announcements_created_by_fkey",
                        column: x => x.created_by,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "announcements_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "announcements_target_group_id_fkey",
                        column: x => x.target_group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "backlog_items",
                schema: "teammy",
                columns: table => new
                {
                    backlog_item_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'planned'::text"),
                    category = table.Column<string>(type: "text", nullable: true),
                    story_points = table.Column<int>(type: "integer", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("backlog_items_pkey", x => x.backlog_item_id);
                    table.ForeignKey(
                        name: "backlog_items_created_by_fkey",
                        column: x => x.created_by,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "backlog_items_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "backlog_items_owner_user_id_fkey",
                        column: x => x.owner_user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "boards",
                schema: "teammy",
                columns: table => new
                {
                    board_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_name = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'Board'::text"),
                    status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("boards_pkey", x => x.board_id);
                    table.ForeignKey(
                        name: "boards_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                schema: "teammy",
                columns: table => new
                {
                    chat_session_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'group'::text"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    participant_a = table.Column<Guid>(type: "uuid", nullable: true),
                    participant_b = table.Column<Guid>(type: "uuid", nullable: true),
                    members = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_sessions_pkey", x => x.chat_session_id);
                    table.ForeignKey(
                        name: "chat_sessions_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                schema: "teammy",
                columns: table => new
                {
                    group_member_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    left_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("group_members_pkey", x => x.group_member_id);
                    table.ForeignKey(
                        name: "group_members_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "group_members_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id");
                    table.ForeignKey(
                        name: "group_members_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "teammy",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invitee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending'::text"),
                    message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("invitations_pkey", x => x.invitation_id);
                    table.ForeignKey(
                        name: "fk_invitations_group",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "invitations_invited_by_fkey",
                        column: x => x.invited_by,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "invitations_invitee_user_id_fkey",
                        column: x => x.invitee_user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "invitations_topic_id_fkey",
                        column: x => x.topic_id,
                        principalSchema: "teammy",
                        principalTable: "topics",
                        principalColumn: "topic_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "milestones",
                schema: "teammy",
                columns: table => new
                {
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'planned'::text"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("milestones_pkey", x => x.milestone_id);
                    table.ForeignKey(
                        name: "milestones_created_by_fkey",
                        column: x => x.created_by,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "milestones_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recruitment_posts",
                schema: "teammy",
                columns: table => new
                {
                    post_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_type = table.Column<string>(type: "text", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    major_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    position_needed = table.Column<string>(type: "text", nullable: true),
                    current_members = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'open'::text"),
                    application_deadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    required_skills = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("recruitment_posts_pkey", x => x.post_id);
                    table.ForeignKey(
                        name: "recruitment_posts_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "recruitment_posts_major_id_fkey",
                        column: x => x.major_id,
                        principalSchema: "teammy",
                        principalTable: "majors",
                        principalColumn: "major_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "recruitment_posts_semester_id_fkey",
                        column: x => x.semester_id,
                        principalSchema: "teammy",
                        principalTable: "semesters",
                        principalColumn: "semester_id");
                    table.ForeignKey(
                        name: "recruitment_posts_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "columns",
                schema: "teammy",
                columns: table => new
                {
                    column_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_name = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("columns_pkey", x => x.column_id);
                    table.ForeignKey(
                        name: "columns_board_id_fkey",
                        column: x => x.board_id,
                        principalSchema: "teammy",
                        principalTable: "boards",
                        principalColumn: "board_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_session_participants",
                schema: "teammy",
                columns: table => new
                {
                    chat_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_session_participants_pkey", x => new { x.chat_session_id, x.user_id });
                    table.ForeignKey(
                        name: "chat_session_participants_session_id_fkey",
                        column: x => x.chat_session_id,
                        principalSchema: "teammy",
                        principalTable: "chat_sessions",
                        principalColumn: "chat_session_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "chat_session_participants_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "teammy",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    chat_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("messages_pkey", x => x.message_id);
                    table.ForeignKey(
                        name: "messages_chat_session_id_fkey",
                        column: x => x.chat_session_id,
                        principalSchema: "teammy",
                        principalTable: "chat_sessions",
                        principalColumn: "chat_session_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "messages_sender_id_fkey",
                        column: x => x.sender_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_member_roles",
                schema: "teammy",
                columns: table => new
                {
                    group_member_role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("group_member_roles_pkey", x => x.group_member_role_id);
                    table.ForeignKey(
                        name: "group_member_roles_group_member_id_fkey",
                        column: x => x.group_member_id,
                        principalSchema: "teammy",
                        principalTable: "group_members",
                        principalColumn: "group_member_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "milestone_items",
                schema: "teammy",
                columns: table => new
                {
                    milestone_item_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    backlog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("milestone_items_pkey", x => x.milestone_item_id);
                    table.ForeignKey(
                        name: "milestone_items_backlog_item_id_fkey",
                        column: x => x.backlog_item_id,
                        principalSchema: "teammy",
                        principalTable: "backlog_items",
                        principalColumn: "backlog_item_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "milestone_items_milestone_id_fkey",
                        column: x => x.milestone_id,
                        principalSchema: "teammy",
                        principalTable: "milestones",
                        principalColumn: "milestone_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "candidates",
                schema: "teammy",
                columns: table => new
                {
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applicant_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending'::text"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("candidates_pkey", x => x.candidate_id);
                    table.ForeignKey(
                        name: "candidates_applicant_group_id_fkey",
                        column: x => x.applicant_group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "candidates_applicant_user_id_fkey",
                        column: x => x.applicant_user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "candidates_applied_by_user_id_fkey",
                        column: x => x.applied_by_user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "candidates_post_id_fkey",
                        column: x => x.post_id,
                        principalSchema: "teammy",
                        principalTable: "recruitment_posts",
                        principalColumn: "post_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "teammy",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    backlog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    sort_order = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("tasks_pkey", x => x.task_id);
                    table.ForeignKey(
                        name: "tasks_backlog_item_id_fkey",
                        column: x => x.backlog_item_id,
                        principalSchema: "teammy",
                        principalTable: "backlog_items",
                        principalColumn: "backlog_item_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "tasks_column_id_fkey",
                        column: x => x.column_id,
                        principalSchema: "teammy",
                        principalTable: "columns",
                        principalColumn: "column_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "tasks_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "teammy",
                columns: table => new
                {
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("comments_pkey", x => x.comment_id);
                    table.ForeignKey(
                        name: "comments_task_id_fkey",
                        column: x => x.task_id,
                        principalSchema: "teammy",
                        principalTable: "tasks",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "comments_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_files",
                schema: "teammy",
                columns: table => new
                {
                    file_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    file_type = table.Column<string>(type: "text", nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("shared_files_pkey", x => x.file_id);
                    table.ForeignKey(
                        name: "shared_files_group_id_fkey",
                        column: x => x.group_id,
                        principalSchema: "teammy",
                        principalTable: "groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "shared_files_task_id_fkey",
                        column: x => x.task_id,
                        principalSchema: "teammy",
                        principalTable: "tasks",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "shared_files_uploaded_by_fkey",
                        column: x => x.uploaded_by,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "task_assignments",
                schema: "teammy",
                columns: table => new
                {
                    task_assignment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("task_assignments_pkey", x => x.task_assignment_id);
                    table.ForeignKey(
                        name: "task_assignments_task_id_fkey",
                        column: x => x.task_id,
                        principalSchema: "teammy",
                        principalTable: "tasks",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "task_assignments_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "teammy",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_actor",
                schema: "teammy",
                table: "activity_logs",
                columns: new[] { "actor_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_group",
                schema: "teammy",
                table: "activity_logs",
                columns: new[] { "group_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_activity_logs_target_user_id",
                schema: "teammy",
                table: "activity_logs",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_index_outbox_processed",
                schema: "teammy",
                table: "ai_index_outbox",
                column: "processed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_ai_index_outbox_type_entity_processed",
                schema: "teammy",
                table: "ai_index_outbox",
                columns: new[] { "type", "entity_id", "processed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_announcements_created_by",
                schema: "teammy",
                table: "announcements",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_semester_id",
                schema: "teammy",
                table: "announcements",
                column: "semester_id");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_target_group_id",
                schema: "teammy",
                table: "announcements",
                column: "target_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_backlog_items_created_by",
                schema: "teammy",
                table: "backlog_items",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_backlog_items_group_status",
                schema: "teammy",
                table: "backlog_items",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_backlog_items_owner",
                schema: "teammy",
                table: "backlog_items",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "boards_group_id_key",
                schema: "teammy",
                table: "boards",
                column: "group_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_candidates_applicant_group_id",
                schema: "teammy",
                table: "candidates",
                column: "applicant_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_candidates_applicant_user_id",
                schema: "teammy",
                table: "candidates",
                column: "applicant_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_candidates_applied_by_user_id",
                schema: "teammy",
                table: "candidates",
                column: "applied_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_cand_post_group",
                schema: "teammy",
                table: "candidates",
                columns: new[] { "post_id", "applicant_group_id" },
                unique: true,
                filter: "(applicant_group_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_cand_post_user",
                schema: "teammy",
                table: "candidates",
                columns: new[] { "post_id", "applicant_user_id" },
                unique: true,
                filter: "(applicant_user_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_chat_session_participants_user",
                schema: "teammy",
                table: "chat_session_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_chat_dm_pair",
                schema: "teammy",
                table: "chat_sessions",
                columns: new[] { "participant_a", "participant_b" },
                unique: true,
                filter: "(type = 'dm'::text)");

            migrationBuilder.CreateIndex(
                name: "ux_chat_project_single",
                schema: "teammy",
                table: "chat_sessions",
                column: "group_id",
                unique: true,
                filter: "(type = 'project'::text)");

            migrationBuilder.CreateIndex(
                name: "columns_board_id_column_name_key",
                schema: "teammy",
                table: "columns",
                columns: new[] { "board_id", "column_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "columns_board_id_position_key",
                schema: "teammy",
                table: "columns",
                columns: new[] { "board_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comments_task_id",
                schema: "teammy",
                table: "comments",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_user_id",
                schema: "teammy",
                table: "comments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_group_member_role",
                schema: "teammy",
                table: "group_member_roles",
                columns: new[] { "group_member_id", "role_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_members_semester_id",
                schema: "teammy",
                table: "group_members",
                column: "semester_id");

            migrationBuilder.CreateIndex(
                name: "ux_group_single_leader",
                schema: "teammy",
                table: "group_members",
                column: "group_id",
                unique: true,
                filter: "(status = 'leader'::text)");

            migrationBuilder.CreateIndex(
                name: "ux_member_user_semester_active",
                schema: "teammy",
                table: "group_members",
                columns: new[] { "user_id", "semester_id" },
                unique: true,
                filter: "(status = ANY (ARRAY['pending'::text, 'member'::text, 'leader'::text]))");

            migrationBuilder.CreateIndex(
                name: "groups_semester_id_name_key",
                schema: "teammy",
                table: "groups",
                columns: new[] { "semester_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_groups_major_id",
                schema: "teammy",
                table: "groups",
                column: "major_id");

            migrationBuilder.CreateIndex(
                name: "IX_groups_mentor_id",
                schema: "teammy",
                table: "groups",
                column: "mentor_id");

            migrationBuilder.CreateIndex(
                name: "ux_group_unique_topic",
                schema: "teammy",
                table: "groups",
                column: "topic_id",
                unique: true,
                filter: "(topic_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_group_id",
                schema: "teammy",
                table: "invitations",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_invited_by",
                schema: "teammy",
                table: "invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_invitee_user_id",
                schema: "teammy",
                table: "invitations",
                column: "invitee_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_topic_id",
                schema: "teammy",
                table: "invitations",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "majors_major_name_key",
                schema: "teammy",
                table: "majors",
                column: "major_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_sender_id",
                schema: "teammy",
                table: "messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_session_created",
                schema: "teammy",
                table: "messages",
                columns: new[] { "chat_session_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "milestone_items_milestone_id_backlog_item_id_key",
                schema: "teammy",
                table: "milestone_items",
                columns: new[] { "milestone_id", "backlog_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_milestone_items_backlog",
                schema: "teammy",
                table: "milestone_items",
                column: "backlog_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_milestones_created_by",
                schema: "teammy",
                table: "milestones",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_milestones_group_status",
                schema: "teammy",
                table: "milestones",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_milestones_target_date",
                schema: "teammy",
                table: "milestones",
                column: "target_date");

            migrationBuilder.CreateIndex(
                name: "ix_posts_semester_status_type",
                schema: "teammy",
                table: "recruitment_posts",
                columns: new[] { "semester_id", "status", "post_type" });

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_posts_group_id",
                schema: "teammy",
                table: "recruitment_posts",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_posts_major_id",
                schema: "teammy",
                table: "recruitment_posts",
                column: "major_id");

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_posts_user_id",
                schema: "teammy",
                table: "recruitment_posts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_rp_required_skills_gin",
                schema: "teammy",
                table: "recruitment_posts",
                column: "required_skills")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "roles_name_key",
                schema: "teammy",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_semesters_season_year",
                schema: "teammy",
                table: "semesters",
                columns: new[] { "season", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shared_files_group_id",
                schema: "teammy",
                table: "shared_files",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_files_task_id",
                schema: "teammy",
                table: "shared_files",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_files_uploaded_by",
                schema: "teammy",
                table: "shared_files",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_skill_aliases_token",
                schema: "teammy",
                table: "skill_aliases",
                column: "token");

            migrationBuilder.CreateIndex(
                name: "IX_task_assignments_user_id",
                schema: "teammy",
                table: "task_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "task_assignments_task_id_user_id_key",
                schema: "teammy",
                table: "task_assignments",
                columns: new[] { "task_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_tasks_column_sort",
                schema: "teammy",
                table: "tasks",
                columns: new[] { "column_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_group_id",
                schema: "teammy",
                table: "tasks",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ux_tasks_backlog_item",
                schema: "teammy",
                table: "tasks",
                column: "backlog_item_id",
                unique: true,
                filter: "(backlog_item_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_topics_created_by",
                schema: "teammy",
                table: "topics",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_topics_major_id",
                schema: "teammy",
                table: "topics",
                column: "major_id");

            migrationBuilder.CreateIndex(
                name: "topics_semester_id_title_key",
                schema: "teammy",
                table: "topics",
                columns: new[] { "semester_id", "title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topics_mentor_mentor_id",
                schema: "teammy",
                table: "topics_mentor",
                column: "mentor_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_status",
                schema: "teammy",
                table: "user_reports",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_reports_target",
                schema: "teammy",
                table: "user_reports",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_reports_assigned_to",
                schema: "teammy",
                table: "user_reports",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "IX_user_reports_reporter_id",
                schema: "teammy",
                table: "user_reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_reports_semester_id",
                schema: "teammy",
                table: "user_reports",
                column: "semester_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_role_id",
                schema: "teammy",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "user_roles_user_id_role_id_key",
                schema: "teammy",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_major_id",
                schema: "teammy",
                table: "users",
                column: "major_id");

            migrationBuilder.CreateIndex(
                name: "users_email_key",
                schema: "teammy",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_logs",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "ai_index_outbox",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "announcements",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "candidates",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "chat_session_participants",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "comments",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "group_member_roles",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "invitations",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "milestone_items",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "semester_policy",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "shared_files",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "skill_aliases",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "task_assignments",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "topics_mentor",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "user_reports",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "recruitment_posts",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "group_members",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "chat_sessions",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "milestones",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "skill_dictionary",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "backlog_items",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "columns",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "boards",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "topics",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "users",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "semesters",
                schema: "teammy");

            migrationBuilder.DropTable(
                name: "majors",
                schema: "teammy");
        }
    }
}
