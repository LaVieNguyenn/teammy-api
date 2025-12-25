using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Teammy.Infrastructure.Persistence;

#nullable disable

namespace Teammy.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20251223120000_AddStudentSemesters")]
    public partial class AddStudentSemesters : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS teammy.student_semesters (
                user_id uuid NOT NULL,
                semester_id uuid NOT NULL,
                is_current boolean NOT NULL DEFAULT false,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT student_semesters_pkey PRIMARY KEY (user_id, semester_id),
                CONSTRAINT student_semesters_user_id_fkey FOREIGN KEY (user_id)
                    REFERENCES teammy.users (user_id) ON DELETE CASCADE,
                CONSTRAINT student_semesters_semester_id_fkey FOREIGN KEY (semester_id)
                    REFERENCES teammy.semesters (semester_id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_student_semesters_current
                ON teammy.student_semesters (user_id)
                WHERE is_current = true;
            """);

            migrationBuilder.Sql("""
            DROP MATERIALIZED VIEW IF EXISTS teammy.mv_students_pool;

            CREATE MATERIALIZED VIEW teammy.mv_students_pool AS
            SELECT
              u.user_id,
              u.display_name,
              u.major_id,
              s.semester_id,
              u.gpa,
              u.desired_position_id,
              p.position_name AS desired_position_name,
              CASE
                WHEN u.skills IS NULL THEN NULL
                WHEN jsonb_typeof(u.skills) = 'array' THEN jsonb_build_object(
                  'skill_tags',
                  COALESCE(
                    (
                      SELECT jsonb_agg(value)
                      FROM jsonb_array_elements(u.skills) AS value
                      WHERE value IS NOT NULL AND jsonb_typeof(value) = 'string'
                    ),
                    '[]'::jsonb
                  )
                )
                WHEN jsonb_typeof(u.skills) = 'string' THEN jsonb_build_object('skill_tags', jsonb_build_array(u.skills))
                ELSE u.skills
              END AS skills,
              COALESCE(
                u.skills->>'primary_role',
                u.skills->>'primaryRole',
                u.skills->>'primary'
              ) AS primary_role,
              u.skills_completed
            FROM teammy.users u
            JOIN teammy.user_roles ur ON ur.user_id = u.user_id
            JOIN teammy.roles r ON r.role_id = ur.role_id AND r.name = 'student'
            JOIN teammy.student_semesters ss ON ss.user_id = u.user_id AND ss.is_current = TRUE
            JOIN teammy.semesters s ON s.semester_id = ss.semester_id
            LEFT JOIN teammy.position_list p ON p.position_id = u.desired_position_id
            LEFT JOIN teammy.group_members gm
              ON gm.user_id = u.user_id
              AND gm.semester_id = s.semester_id
              AND gm.status IN ('pending','member','leader')
            WHERE gm.group_member_id IS NULL;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP MATERIALIZED VIEW IF EXISTS teammy.mv_students_pool;

            CREATE MATERIALIZED VIEW teammy.mv_students_pool AS
            SELECT
              u.user_id,
              u.display_name,
              u.major_id,
              s.semester_id,
              u.gpa,
              u.desired_position_id,
              p.position_name AS desired_position_name,
              CASE
                WHEN u.skills IS NULL THEN NULL
                WHEN jsonb_typeof(u.skills) = 'array' THEN jsonb_build_object(
                  'skill_tags',
                  COALESCE(
                    (
                      SELECT jsonb_agg(value)
                      FROM jsonb_array_elements(u.skills) AS value
                      WHERE value IS NOT NULL AND jsonb_typeof(value) = 'string'
                    ),
                    '[]'::jsonb
                  )
                )
                WHEN jsonb_typeof(u.skills) = 'string' THEN jsonb_build_object('skill_tags', jsonb_build_array(u.skills))
                ELSE u.skills
              END AS skills,
              COALESCE(
                u.skills->>'primary_role',
                u.skills->>'primaryRole',
                u.skills->>'primary'
              ) AS primary_role,
              u.skills_completed
            FROM teammy.users u
            JOIN teammy.user_roles ur ON ur.user_id = u.user_id
            JOIN teammy.roles r ON r.role_id = ur.role_id AND r.name = 'student'
            JOIN teammy.semesters s ON s.is_active = TRUE
            LEFT JOIN teammy.position_list p ON p.position_id = u.desired_position_id
            LEFT JOIN teammy.group_members gm
              ON gm.user_id = u.user_id
              AND gm.semester_id = s.semester_id
              AND gm.status IN ('pending','member','leader')
            WHERE gm.group_member_id IS NULL;

            DROP TABLE IF EXISTS teammy.student_semesters;
            """);
        }
    }
}
