using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Skills.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ISkillDictionaryReadOnlyQueries
{
    Task<IReadOnlyList<SkillDictionaryDto>> ListAsync(string? role, string? major, CancellationToken ct);
    Task<SkillDictionaryDto?> GetByTokenAsync(string token, CancellationToken ct);
}
