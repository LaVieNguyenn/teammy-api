using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teammy.Application.Common.Interfaces.Auth;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Interfaces.Topics;
using Teammy.Application.Mentors;
using Teammy.Application.Topics;
using Teammy.Infrastructure.Auth;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Repositories;
using Teammy.Infrastructure.Topics;

namespace Teammy.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Default")));
            services.AddScoped<IExternalTokenVerifier, FirebaseTokenVerifier>();
            services.AddSingleton<ITokenService, JwtTokenService>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IMentorService, MentorService>();
            services.AddScoped<IMentorRepository, MentorRepository>();
            services.AddScoped<ITopicRepository, TopicRepository>();
            services.AddScoped<ITopicService, TopicService>();
            services.AddScoped<ITopicImportService, TopicImportService>();
            return services;
        }
    }
}