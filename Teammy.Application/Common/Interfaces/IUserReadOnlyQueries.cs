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
            Guid? majorId,
            bool requireStudentRole,
            int limit,
            CancellationToken ct);

        Task<IReadOnlyList<AdminUserListItemDto>> GetAllForAdminAsync(CancellationToken ct);

        Task<AdminUserDetailDto?> GetAdminDetailAsync(Guid userId, CancellationToken ct);

    }
}
