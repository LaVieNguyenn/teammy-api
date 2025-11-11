namespace Teammy.Application.Common.Interfaces;

public interface IUserWriteRepository
{
    Task<bool> EmailExistsAnyAsync(string email, CancellationToken ct);
    Task<Guid> CreateUserAsync(string email, string displayName,
                               string? studentCode, string? gender,
                               Guid? majorId,
                               CancellationToken ct);

    Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct);
}
