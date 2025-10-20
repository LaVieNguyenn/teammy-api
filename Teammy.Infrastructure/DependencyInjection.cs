using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teammy.Application.Common.Interfaces.Auth;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Mentors;
using Teammy.Infrastructure.Auth;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Repositories;

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
            return services;
        }
    }
}