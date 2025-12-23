using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teammy.Infrastructure.Migrations
{
    public partial class UpdateAnnouncementScopeCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            ALTER TABLE teammy.announcements
                DROP CONSTRAINT IF EXISTS announcements_scope_check;
            ALTER TABLE teammy.announcements
                ADD CONSTRAINT announcements_scope_check CHECK (
                    scope IN ('global','semester','role','group','groups_without_topic','groups_understaffed','students_without_group')
                );
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            ALTER TABLE teammy.announcements
                DROP CONSTRAINT IF EXISTS announcements_scope_check;
            ALTER TABLE teammy.announcements
                ADD CONSTRAINT announcements_scope_check CHECK (
                    scope IN ('global','semester','role','group')
                );
            """);
        }
    }
}
