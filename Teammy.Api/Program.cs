using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Teammy.Application.Auth.Queries;
using Teammy.Application.Auth.Services;
using Teammy.Application.Groups.Services;
using Teammy.Application.Posts.Services;
using Teammy.Application.Invitations.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// SignalR removed per request

// Application services
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<CurrentUserQueryService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<RecruitmentPostService>();
builder.Services.AddScoped<ProfilePostService>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddSingleton<IAppUrlProvider, Teammy.Api.App.AppUrlProvider>();

// Infrastructure (DbContext, Auth services, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

// JWT
var key = builder.Configuration["Auth:Jwt:Key"]!;
var issuer = builder.Configuration["Auth:Jwt:Issuer"]!;
var audience = builder.Configuration["Auth:Jwt:Audience"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseSwagger(); app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
