using Microsoft.EntityFrameworkCore;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence;

public partial class AppDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<position_list>(entity =>
        {
            entity.HasKey(e => e.position_id).HasName("position_list_pkey");

            entity.ToTable("position_list", "teammy");

            entity.Property(e => e.position_id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at).HasDefaultValueSql("now()");

            entity.HasIndex(e => new { e.major_id, e.position_name }, "position_list_major_id_position_name_key").IsUnique();

            entity.HasOne(d => d.major).WithMany()
                .HasForeignKey(d => d.major_id)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("position_list_major_id_fkey");
        });

        modelBuilder.Entity<user>(entity =>
        {
            entity.HasOne(d => d.desired_position).WithMany(p => p.users)
                .HasForeignKey(d => d.desired_position_id)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("users_desired_position_id_fkey");
        });
    }
}
