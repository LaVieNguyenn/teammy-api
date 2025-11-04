using System;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Auth.Dtos;

namespace Teammy.Application.Common.Interfaces
{
    public interface IUserReadOnlyQueries
    {
        Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct);
    }
}
