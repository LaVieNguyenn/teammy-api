using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Files;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Application.Kanban.Services;
using Teammy.Application.Topics.Services;
using Teammy.Application.Semesters.Services;
using Teammy.Application.Users.Services;
using Teammy.Infrastructure.Auth;
using Teammy.Infrastructure.Email;
using Teammy.Infrastructure.Excel;
using Teammy.Infrastructure.Files;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Repositories;
using Teammy.Infrastructure.Topics;
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
        services.AddScoped<IUserWriteRepository, UserWriteRepository>();
        services.AddScoped<UserProfileService>();

        // Groups
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IGroupReadOnlyQueries, GroupReadOnlyQueries>();

        // Recruitment/Profile posts
        services.AddScoped<IRecruitmentPostRepository, RecruitmentPostRepository>();
        services.AddScoped<IRecruitmentPostReadOnlyQueries, RecruitmentPostReadOnlyQueries>();
        services.AddScoped<IChatRepository, ChatRepository>();

        // Invitations
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IInvitationReadOnlyQueries, InvitationReadOnlyQueries>();

        // Excel import
        services.AddScoped<IUserImportService, ExcelUserImportService>();

        // Topics
        services.AddScoped<ITopicReadOnlyQueries, TopicReadOnlyQueries>();
        services.AddScoped<ITopicWriteRepository, TopicWriteRepository>();
        services.AddScoped<ITopicImportService, ExcelTopicImportService>();
        services.AddScoped<TopicsService>();
        services.AddScoped<IMentorLookupService, MentorLookupService>();
        services.AddScoped<ITopicMentorService, TopicMentorService>();

        // Roles
        services.AddScoped<IRoleReadOnlyQueries, RoleReadOnlyQueries>();

        // Majors
        services.AddScoped<IMajorReadOnlyQueries, MajorReadOnlyQueries>();
        services.AddScoped<IMajorWriteRepository, MajorWriteRepository>();

        // Semesters
        services.AddScoped<ISemesterWriteRepository, SemesterWriteRepository>();

        // Kanban
        services.AddScoped<IKanbanReadOnlyQueries, KanbanReadOnlyQueries>();
        services.AddScoped<IKanbanRepository, KanbanRepository>();
        services.AddScoped<IGroupAccessQueries, GroupAccessQueries>();
        services.AddScoped<IFileStorage, GoogleDriveStorage>();
        services.AddScoped<KanbanService>();

        // Semester & Semester policies
        services.AddScoped<ISemesterRepository, SemesterRepository>();
        services.AddScoped<ISemesterReadOnlyQueries, SemesterReadOnlyQueries>();
        services.AddScoped<SemesterService>(); 
        
        // Semester phase guard
        services.AddScoped<SemesterPhaseGuard>();
        return services;
    }
}
