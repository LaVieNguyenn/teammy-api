using Teammy.Domain.Users;

namespace Teammy.Application.Common.Interfaces;

public interface IUserRepository
{
    Task<User?> FindActiveByEmailAsync(string email, CancellationToken ct);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
}
