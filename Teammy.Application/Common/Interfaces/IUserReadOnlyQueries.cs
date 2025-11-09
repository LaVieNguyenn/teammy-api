using System;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Auth.Dtos;
using Teammy.Application.Users.Dtos;

namespace Teammy.Application.Common.Interfaces
{
    public interface IUserReadOnlyQueries
    {
        Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct);

        Task<IReadOnlyList<UserSearchDto>> SearchInvitableAsync(
            string? query,
            Guid semesterId,
            int limit,
            CancellationToken ct);
    }
}
