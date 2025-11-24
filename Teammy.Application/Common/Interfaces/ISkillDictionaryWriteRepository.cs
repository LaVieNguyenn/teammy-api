using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces;

public interface ISkillDictionaryWriteRepository
{
    Task<bool> TokenExistsAsync(string token, CancellationToken ct);
    Task CreateAsync(string token, string role, string major, IReadOnlyList<string> aliases, CancellationToken ct);
    Task UpdateAsync(string token, string role, string major, IReadOnlyList<string> aliases, CancellationToken ct);
    Task DeleteAsync(string token, CancellationToken ct);
}
