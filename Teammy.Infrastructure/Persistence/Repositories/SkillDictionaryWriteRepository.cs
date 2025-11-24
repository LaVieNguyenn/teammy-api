using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class SkillDictionaryWriteRepository : ISkillDictionaryWriteRepository
{
    private readonly AppDbContext _db;

    public SkillDictionaryWriteRepository(AppDbContext db) => _db = db;

    public Task<bool> TokenExistsAsync(string token, CancellationToken ct)
    {
        var normalizedToken = token.Trim();
        return _db.skill_dictionaries.AnyAsync(x => x.token == normalizedToken, ct);
    }

    public async Task CreateAsync(string token, string role, string major, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        var entity = new skill_dictionary
        {
            token = token,
            role = role,
            major = major,
            skill_aliases = aliases
                .Select(alias => new skill_alias
                {
                    alias = alias,
                    token = token
                })
                .ToList()
        };

        _db.skill_dictionaries.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(string token, string role, string major, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        var entity = await _db.skill_dictionaries
            .Include(x => x.skill_aliases)
            .FirstOrDefaultAsync(x => x.token == token, ct)
            ?? throw new KeyNotFoundException("Skill token not found.");

        entity.role = role;
        entity.major = major;

        var desired = new HashSet<string>(aliases, System.StringComparer.OrdinalIgnoreCase);

        var existing = entity.skill_aliases.ToList();
        foreach (var alias in existing)
        {
            if (!desired.Contains(alias.alias))
                _db.skill_aliases.Remove(alias);
        }

        var existingSet = existing
            .Select(x => x.alias)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        foreach (var alias in desired)
        {
            if (!existingSet.Contains(alias))
            {
                entity.skill_aliases.Add(new skill_alias
                {
                    alias = alias,
                    token = token
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string token, CancellationToken ct)
    {
        var entity = await _db.skill_dictionaries
            .Include(x => x.skill_aliases)
            .FirstOrDefaultAsync(x => x.token == token, ct);

        if (entity is null)
            return;

        _db.skill_aliases.RemoveRange(entity.skill_aliases);
        _db.skill_dictionaries.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
