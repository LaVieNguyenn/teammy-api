namespace Teammy.Domain.Users;

public sealed class User
{
    public Guid Id { get; }
    public string Email { get; }
    public string DisplayName { get; }
    public string? AvatarUrl { get; }
    public bool EmailVerified { get; }
    public bool SkillsCompleted { get; }
    public bool IsActive { get; }
    public string RoleName { get; }

    public User(Guid id, string email, string displayName, string? avatarUrl,
                bool emailVerified, bool skillsCompleted, bool isActive, string roleName)
    {
        Id = id; Email = email; DisplayName = displayName; AvatarUrl = avatarUrl;
        EmailVerified = emailVerified; SkillsCompleted = skillsCompleted;
        IsActive = isActive; RoleName = roleName;
    }
}
