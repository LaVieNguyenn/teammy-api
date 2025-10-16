namespace Teammy.Application.Common.Interfaces.Persistence;

public sealed class UserReadModel
{
    public Guid Id { get; init; }
    public string Email { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? PhotoUrl { get; set; }
    public string RoleName { get; set; } = default!;
    public bool IsActive { get; set; }
}

public interface IUserRepository
{
    Task<UserReadModel?> FindByEmailAsync(string email, bool includeRole, CancellationToken ct);
    Task<UserReadModel?> FindByIdAsync(Guid id, bool includeRole, CancellationToken ct);
    Task SyncDisplayAsync(Guid userId, string email, string displayName, string? photoUrl, bool emailVerified, CancellationToken ct);
}
