using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teammy.Infrastructure.Migrations
{
    public partial class AddAnnouncementTargetsArray : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            ALTER TABLE teammy.announcements
                DROP CONSTRAINT IF EXISTS announcements_target_group_id_fkey;
            ALTER TABLE teammy.announcements
                DROP COLUMN IF EXISTS target_group_id;
            ALTER TABLE teammy.announcements
                ADD COLUMN IF NOT EXISTS target_group_ids uuid[];
            ALTER TABLE teammy.announcements
                ADD COLUMN IF NOT EXISTS target_user_ids uuid[];
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            ALTER TABLE teammy.announcements
                DROP COLUMN IF EXISTS target_group_ids;
            ALTER TABLE teammy.announcements
                DROP COLUMN IF EXISTS target_user_ids;
            ALTER TABLE teammy.announcements
                ADD COLUMN IF NOT EXISTS target_group_id uuid;
            ALTER TABLE teammy.announcements
                ADD CONSTRAINT announcements_target_group_id_fkey
                FOREIGN KEY (target_group_id) REFERENCES teammy.groups(group_id) ON DELETE CASCADE;
            """);
        }
    }
}
