using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teammy.Application.Announcements.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Files;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Application.Ai.Services;
using Teammy.Application.Kanban.Services;
using Teammy.Application.Topics.Services;
using Teammy.Application.Semesters.Services;
using Teammy.Application.Skills.Services;
using Teammy.Application.Users.Services;
using Teammy.Application.ProjectTracking.Interfaces;
using Teammy.Application.ProjectTracking.Services;
using Teammy.Application.Reports;
using Teammy.Infrastructure.Auth;
using Teammy.Infrastructure.Email;
using Teammy.Infrastructure.Excel;
using Teammy.Infrastructure.Files;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Repositories;
using Teammy.Infrastructure.Topics;
using Teammy.Infrastructure.Reports;
using Teammy.Infrastructure.Ai;
using Teammy.Infrastructure.Ai.Indexing;
namespace Teammy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AiIndexOutboxSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(configuration.GetConnectionString("Default"));
            opt.AddInterceptors(sp.GetRequiredService<AiIndexOutboxSaveChangesInterceptor>());
        });

        services.AddSingleton<IExternalTokenVerifier, FirebaseTokenVerifier>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        // Email
        services.AddSingleton<IEmailSender, HttpEmailSender>();

        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
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
        services.AddScoped<IDashboardReadOnlyQueries, DashboardReadOnlyQueries>();

        // Invitations
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IInvitationReadOnlyQueries, InvitationReadOnlyQueries>();

        // Excel import
        services.AddScoped<IUserImportService, ExcelUserImportService>();

        // Topics
        services.AddScoped<ITopicReadOnlyQueries, TopicReadOnlyQueries>();
        services.AddScoped<ITopicWriteRepository, TopicWriteRepository>();
        services.AddScoped<ITopicImportService, TopicRegistrationPackageImportService>();
        services.AddScoped<TopicsService>();
        services.AddScoped<IMentorLookupService, MentorLookupService>();
        services.AddScoped<ITopicMentorService, TopicMentorService>();

        // Roles
        services.AddScoped<IRoleReadOnlyQueries, RoleReadOnlyQueries>();

        // Majors
        services.AddScoped<IMajorReadOnlyQueries, MajorReadOnlyQueries>();
        services.AddScoped<IMajorWriteRepository, MajorWriteRepository>();

        // Positions
        services.AddScoped<IPositionReadOnlyQueries, PositionReadOnlyQueries>();
        services.AddScoped<IPositionWriteRepository, PositionWriteRepository>();

        // Semesters
        services.AddScoped<ISemesterWriteRepository, SemesterWriteRepository>();

        // Kanban
        services.AddScoped<IKanbanReadOnlyQueries, KanbanReadOnlyQueries>();
        services.AddScoped<IKanbanRepository, KanbanRepository>();
        services.AddScoped<IGroupAccessQueries, GroupAccessQueries>();
        services.AddScoped<IFileStorage, GoogleDriveStorage>();
        services.AddScoped<KanbanService>();

        // Project tracking
        services.AddScoped<IProjectTrackingReadOnlyQueries, ProjectTrackingReadOnlyQueries>();
        services.AddScoped<IProjectTrackingRepository, ProjectTrackingRepository>();
        services.AddScoped<ProjectTrackingService>();

        // Semester & Semester policies
        services.AddScoped<ISemesterRepository, SemesterRepository>();
        services.AddScoped<ISemesterReadOnlyQueries, SemesterReadOnlyQueries>();
        services.AddScoped<SemesterService>();

        // Skills
        services.AddScoped<ISkillDictionaryReadOnlyQueries, SkillDictionaryQueries>();
        services.AddScoped<ISkillDictionaryWriteRepository, SkillDictionaryWriteRepository>();
        services.AddScoped<SkillDictionaryService>();

        // Announcements
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IAnnouncementReadOnlyQueries, AnnouncementReadOnlyQueries>();
        services.AddScoped<IAnnouncementRecipientQueries, AnnouncementRecipientQueries>();
        services.AddScoped<AnnouncementService>();
        services.AddScoped<Teammy.Application.Announcements.Services.AnnouncementPlanningOverviewService>();

        // AI Matching
        services.AddScoped<IAiMatchingQueries, AiMatchingQueries>();
        services.AddScoped<AiMatchingService>();

        // Semester phase guard
        services.AddScoped<SemesterPhaseGuard>();

        // Reports
        services.AddScoped<IReportExportService, ExcelReportExportService>();

        services.AddScoped<IAiIndexSourceQueries, AiIndexSourceQueries>();

        services.AddHttpClient<IAiSemanticSearch, AiSemanticSearchClient>(client =>
        {
            ConfigureAiGatewayClient(client, configuration, timeoutSeconds: 20);
        });

        services.AddHttpClient<IAiLlmClient, AiLlmClient>(client =>
        {
            ConfigureAiGatewayClient(client, configuration, timeoutSeconds: 90);
        });

        services.AddHttpClient<AiGatewayClient>(client =>
        {
            ConfigureAiGatewayClient(client, configuration, timeoutSeconds: 45);
        });


        services.AddHostedService<AiIndexOutboxWorker>();
        return services;

        static void ConfigureAiGatewayClient(HttpClient client, IConfiguration configuration, int timeoutSeconds)
        {
            var baseUrl = configuration["AI_GATEWAY_BASE_URL"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("AI_GATEWAY_BASE_URL is not configured.");

            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }
    }
}
