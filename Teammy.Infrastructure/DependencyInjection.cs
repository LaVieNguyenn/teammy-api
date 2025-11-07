using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Auth;
using Teammy.Infrastructure.Email;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Repositories;

namespace Teammy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(configuration.GetConnectionString("Default")));

        services.AddSingleton<IExternalTokenVerifier, FirebaseTokenVerifier>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        // Choose email provider: HTTP API (SendGrid/Resend) or SMTP (default)
        var provider = (configuration["Email:Provider"] ?? configuration["Email:Http:Provider"] ?? "").Trim().ToLowerInvariant();
        if (provider == "sendgrid" || provider == "resend" || provider == "http")
            services.AddSingleton<IEmailSender, HttpEmailSender>();
        else
            services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserReadOnlyQueries, UserReadOnlyQueries>();

        // Groups
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IGroupReadOnlyQueries, GroupReadOnlyQueries>();

        // Recruitment/Profile posts
        services.AddScoped<IRecruitmentPostRepository, RecruitmentPostRepository>();
        services.AddScoped<IRecruitmentPostReadOnlyQueries, RecruitmentPostReadOnlyQueries>();

        // Invitations
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IInvitationReadOnlyQueries, InvitationReadOnlyQueries>();

        return services;
    }
}
