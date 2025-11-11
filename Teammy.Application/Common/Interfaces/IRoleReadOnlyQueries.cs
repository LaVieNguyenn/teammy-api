namespace Teammy.Application.Common.Interfaces;

public interface IRoleReadOnlyQueries
{
    Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken ct);
    Task<Guid?> GetRoleIdByNameAsync(string roleName, CancellationToken ct);
}
