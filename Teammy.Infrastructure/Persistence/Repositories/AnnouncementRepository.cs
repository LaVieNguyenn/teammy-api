using Microsoft.EntityFrameworkCore;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class AnnouncementRepository(AppDbContext db) : IAnnouncementRepository
{
    public async Task<AnnouncementDto> CreateAsync(CreateAnnouncementCommand command, CancellationToken ct)
    {
        var entity = new announcement
        {
            announcement_id = Guid.NewGuid(),
            semester_id = command.SemesterId,
            scope = command.Scope,
            target_role = command.TargetRole,
            target_group_id = command.TargetGroupId,
            title = command.Title,
            content = command.Content,
            pinned = command.Pinned,
            publish_at = command.PublishAt,
            expire_at = command.ExpireAt,
            created_by = command.CreatedBy
        };

        await db.announcements.AddAsync(entity, ct);
        await db.SaveChangesAsync(ct);

        var creatorName = await db.users.AsNoTracking()
            .Where(u => u.user_id == command.CreatedBy)
            .Select(u => u.display_name ?? u.email ?? string.Empty)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return new AnnouncementDto(
            entity.announcement_id,
            entity.semester_id,
            entity.scope,
            entity.target_role,
            entity.target_group_id,
            entity.title,
            entity.content,
            entity.pinned,
            entity.publish_at,
            entity.expire_at,
            entity.created_by,
            creatorName);
    }
}
