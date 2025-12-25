using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Skills.Dtos;

namespace Teammy.Application.Skills.Services;

public sealed class SkillDictionaryService(
    ISkillDictionaryReadOnlyQueries read,
    ISkillDictionaryWriteRepository write)
{
    public Task<IReadOnlyList<SkillDictionaryDto>> ListAsync(string? role, string? major, CancellationToken ct)
    {
        var normalizedRole = NormalizeOptional(role);
        var normalizedMajor = NormalizeOptional(major);
        return read.ListAsync(normalizedRole, normalizedMajor, ct);
    }

    public async Task<SkillDictionaryDto> GetByTokenAsync(string token, CancellationToken ct)
    {
        var normalizedToken = NormalizeRequired(token, nameof(token));
        var dto = await read.GetByTokenAsync(normalizedToken, ct);
        return dto ?? throw new KeyNotFoundException("Skill token not found.");
    }

    public async Task<SkillDictionaryDto> CreateAsync(CreateSkillDictionaryRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var token = NormalizeRequired(request.Token, nameof(request.Token));
        var role = NormalizeRequired(request.Role, nameof(request.Role));
        var major = NormalizeRequired(request.Major, nameof(request.Major));
        var aliases = NormalizeAliases(request.Aliases);

        if (await write.TokenExistsAsync(token, ct))
            throw new InvalidOperationException("Skill token already exists.");

        await EnsureAliasesAvailableAsync(token, aliases, ct);
        await write.CreateAsync(token, role, major, aliases, ct);
        return await GetByTokenAsync(token, ct);
    }

    public async Task<SkillDictionaryDto> UpdateAsync(string token, UpdateSkillDictionaryRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var normalizedToken = NormalizeRequired(token, nameof(token));
        var role = NormalizeRequired(request.Role, nameof(request.Role));
        var major = NormalizeRequired(request.Major, nameof(request.Major));
        var aliases = NormalizeAliases(request.Aliases);

        if (!await write.TokenExistsAsync(normalizedToken, ct))
            throw new KeyNotFoundException("Skill token not found.");

        await EnsureAliasesAvailableAsync(normalizedToken, aliases, ct);
        await write.UpdateAsync(normalizedToken, role, major, aliases, ct);
        return await GetByTokenAsync(normalizedToken, ct);
    }

    public async Task DeleteAsync(string token, CancellationToken ct)
    {
        var normalizedToken = NormalizeRequired(token, nameof(token));

        if (!await write.TokenExistsAsync(normalizedToken, ct))
            throw new KeyNotFoundException("Skill token not found.");

        await write.DeleteAsync(normalizedToken, ct);
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} is required.", paramName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeAliases(IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
            return Array.Empty<string>();

        var normalized = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            var trimmed = alias.Trim();
            if (set.Add(trimmed))
                normalized.Add(trimmed);
        }

        return normalized;
    }

    private async Task EnsureAliasesAvailableAsync(string token, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        foreach (var alias in aliases)
        {
            var existing = await read.GetTokenByAliasAsync(alias, ct);
            if (existing is null)
                continue;

            if (!string.Equals(existing, token, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Alias already used by token: {existing}.");
        }
    }
}
