using Teammy.Domain.Users;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Mapping;

internal static class PersistenceToDomainMapper
{
    public static User ToDomainUser(user u, string roleName) =>
        new(
            id: u.user_id,
            email: u.email!,
            displayName: u.display_name!,
            avatarUrl: u.avatar_url,
            emailVerified: u.email_verified,
            skillsCompleted: u.skills_completed,
            isActive: u.is_active,
            roleName: roleName
        );
}
