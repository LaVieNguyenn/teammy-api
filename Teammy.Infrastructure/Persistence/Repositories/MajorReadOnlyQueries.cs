using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class MajorReadOnlyQueries(AppDbContext db) : IMajorReadOnlyQueries
{
    public async Task<IReadOnlyList<string>> GetAllMajorNamesAsync(CancellationToken ct)
        => await db.majors.AsNoTracking().OrderBy(m => m.major_name).Select(m => m.major_name).ToListAsync(ct);

    public async Task<Guid?> FindMajorIdByNameAsync(string majorName, CancellationToken ct)
        => await db.majors.AsNoTracking()
               .Where(m => m.major_name.ToLower() == majorName.ToLower())
               .Select(m => (Guid?)m.major_id)
               .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<(Guid MajorId, string MajorName)>> ListAsync(CancellationToken ct)
        => await db.majors.AsNoTracking()
               .OrderBy(m => m.major_name)
               .Select(m => new ValueTuple<Guid, string>(m.major_id, m.major_name))
               .ToListAsync(ct);

    public async Task<(Guid MajorId, string MajorName)?> GetAsync(Guid id, CancellationToken ct)
        => await db.majors.AsNoTracking()
               .Where(m => m.major_id == id)
               .Select(m => new ValueTuple<Guid, string>(m.major_id, m.major_name))
               .FirstOrDefaultAsync(ct)
               .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string>?)null : t.Result, ct);
}
