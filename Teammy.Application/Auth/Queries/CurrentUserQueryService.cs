using Teammy.Application.Auth.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Auth.Queries;

public sealed class CurrentUserQueryService(IUserReadOnlyQueries queries)
{
    public Task<CurrentUserDto?> GetAsync(Guid userId, CancellationToken ct)
        => queries.GetCurrentUserAsync(userId, ct);
}
