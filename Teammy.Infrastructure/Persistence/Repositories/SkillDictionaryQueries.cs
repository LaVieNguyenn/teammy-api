using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Skills.Dtos;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class SkillDictionaryQueries : ISkillDictionaryReadOnlyQueries
{
    private readonly AppDbContext _db;

    public SkillDictionaryQueries(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SkillDictionaryDto>> ListAsync(string? role, string? major, CancellationToken ct)
    {
        var query = _db.skill_dictionaries
            .AsNoTracking()
            .Include(x => x.skill_aliases)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = role.Trim();
            query = query.Where(x => x.role == normalizedRole);
        }

        if (!string.IsNullOrWhiteSpace(major))
        {
            var normalizedMajor = major.Trim();
            query = query.Where(x => x.major == normalizedMajor);
        }

        var entities = await query
            .OrderBy(x => x.token)
            .ToListAsync(ct);

        return entities
            .Select(Map)
            .ToList();
    }

    public async Task<SkillDictionaryDto?> GetByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var normalizedToken = token.Trim();

        var entity = await _db.skill_dictionaries
            .AsNoTracking()
            .Include(x => x.skill_aliases)
            .FirstOrDefaultAsync(x => x.token == normalizedToken, ct);

        return entity is null ? null : Map(entity);
    }

    public async Task<string?> GetTokenByAliasAsync(string alias, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        var normalized = alias.Trim();
        return await _db.skill_aliases.AsNoTracking()
            .Where(x => x.alias == normalized)
            .Select(x => x.token)
            .FirstOrDefaultAsync(ct);
    }

    private static SkillDictionaryDto Map(Persistence.Models.skill_dictionary entity)
    {
        var aliases = entity.skill_aliases
            .OrderBy(a => a.alias)
            .Select(a => a.alias)
            .ToList();

        return new SkillDictionaryDto(
            entity.token,
            entity.role,
            entity.major,
            aliases
        );
    }
}
