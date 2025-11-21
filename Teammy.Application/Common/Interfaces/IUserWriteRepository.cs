namespace Teammy.Application.Common.Interfaces;

public interface IUserWriteRepository
{
    Task<bool> EmailExistsAnyAsync(string email, CancellationToken ct);
    Task<Guid> CreateUserAsync(string email, string displayName,
                               string? studentCode, string? gender,
                               Guid? majorId,
                               CancellationToken ct);

    Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct);
    Task UpdateUserAsync(
    Guid userId,
    string displayName,
    string? studentCode,
    string? gender,
    Guid? majorId,
    bool isActive,
    CancellationToken ct);

    Task DeleteUserAsync(Guid userId, CancellationToken ct);
    Task SetSingleRoleAsync(Guid userId, Guid roleId, CancellationToken ct);

    Task UpdateProfileAsync(
        Guid userId,
        string displayName,
        string? phone,
        string? studentCode,
        string? gender,
        Guid? majorId,
        string? skillsJson,
        bool skillsCompleted,
        CancellationToken ct);

    Task UpdateAvatarAsync(Guid userId, string avatarUrl, CancellationToken ct);

}
