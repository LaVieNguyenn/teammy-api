using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class MajorWriteRepository(AppDbContext db) : IMajorWriteRepository
{
    public async Task<Guid> CreateAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required");
        var exists = await db.majors.AnyAsync(m => m.major_name.ToLower() == name.ToLower(), ct);
        if (exists) throw new InvalidOperationException("Major name already exists");

        var e = new major { major_id = Guid.NewGuid(), major_name = name.Trim() };
        db.majors.Add(e);
        await db.SaveChangesAsync(ct);
        return e.major_id;
    }

    public async Task UpdateAsync(Guid id, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required");
        var e = await db.majors.FirstOrDefaultAsync(x => x.major_id == id, ct)
              ?? throw new KeyNotFoundException("Major not found");
        if (!string.Equals(e.major_name, name, StringComparison.Ordinal))
        {
            var exists = await db.majors.AnyAsync(m => m.major_id != id && m.major_name.ToLower() == name.ToLower(), ct);
            if (exists) throw new InvalidOperationException("Major name already exists");
        }
        e.major_name = name.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var e = await db.majors.FirstOrDefaultAsync(x => x.major_id == id, ct)
              ?? throw new KeyNotFoundException("Major not found");
        db.majors.Remove(e);
        await db.SaveChangesAsync(ct);
    }
}

