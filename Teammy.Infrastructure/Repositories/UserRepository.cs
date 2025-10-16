using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public async Task<UserReadModel?> FindByEmailAsync(string email, bool includeRole, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        // CITEXT trên Postgres lo phần case-insensitive, không cần ToLower()
        var q = _db.users.AsNoTracking();
        if (includeRole) q = q.Include(u => u.role);

        var u = await q.FirstOrDefaultAsync(x => x.email == email, ct);
        if (u is null) return null;

        return new UserReadModel
        {
            Id = u.id,
            Email = u.email,
            DisplayName = u.display_name,
            PhotoUrl = u.photo_url,
            RoleName = u.role.name,
            IsActive = u.is_active
        };
    }

    public async Task<UserReadModel?> FindByIdAsync(Guid id, bool includeRole, CancellationToken ct)
    {
        var q = _db.users.AsNoTracking();
        if (includeRole) q = q.Include(u => u.role);

        var u = await q.FirstOrDefaultAsync(x => x.id == id, ct);
        if (u is null) return null;

        return new UserReadModel
        {
            Id = u.id,
            Email = u.email,
            DisplayName = u.display_name,
            PhotoUrl = u.photo_url,
            RoleName = u.role.name,
            IsActive = u.is_active
        };
    }

    public async Task SyncDisplayAsync(Guid userId, string email, string displayName, string? photoUrl, bool emailVerified, CancellationToken ct)
    {
        var u = await _db.users.FirstOrDefaultAsync(x => x.id == userId, ct);
        if (u is null) return;

        bool dirty = false;
        if (u.email != email) { u.email = email; dirty = true; }
        if (u.display_name != displayName) { u.display_name = displayName; dirty = true; }
        if (u.photo_url != photoUrl) { u.photo_url = photoUrl; dirty = true; }
        if (u.email_verified != emailVerified) { u.email_verified = emailVerified; dirty = true; }

        if (dirty) await _db.SaveChangesAsync(ct);
    }
}
