using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Linq;
using System.Text;
using Teammy.Application.Activity.Services;
using Microsoft.Extensions.Configuration.Json;
using Teammy.Application.Auth.Queries;
using Teammy.Application.Auth.Services;
using Teammy.Api.Hubs;
using Teammy.Application.Chat.Services;
using Teammy.Application.Groups.Services;
using Teammy.Application.Posts.Services;
using Teammy.Application.Invitations.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Render's free tier limits inotify watchers; disable config file hot reload to avoid hitting the limit.
foreach (var jsonSource in builder.Configuration.Sources.OfType<JsonConfigurationSource>())
    jsonSource.ReloadOnChange = false;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TEAMMY API", Version = "v1" });

    var jwtSecurityScheme = new OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Description = "Put only your JWT token here (Swagger will prefix with 'Bearer ').",
        Reference = new OpenApiReference
        {
            Id = "Bearer",
            Type = ReferenceType.SecurityScheme
        }
    };

    c.AddSecurityDefinition("Bearer", jwtSecurityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtSecurityScheme, Array.Empty<string>() }
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("TeammyPolicy", policy =>
    {
        policy
            .WithOrigins(
                "https://teammy.vercel.app",
                "http://localhost:5173"   
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Application services
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<CurrentUserQueryService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<RecruitmentPostService>();
builder.Services.AddScoped<ProfilePostService>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<GroupChatService>();
builder.Services.AddScoped<ChatConversationService>();
builder.Services.AddScoped<ChatSessionMessageService>();
builder.Services.AddScoped<IGroupChatNotifier, GroupChatNotifier>();
builder.Services.AddScoped<IInvitationNotifier, InvitationNotifier>();
builder.Services.AddScoped<IAnnouncementNotifier, AnnouncementNotifier>();
builder.Services.AddScoped<IActivityLogNotifier, ActivityLogNotifier>();
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
app.UseCors("TeammyPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<GroupChatHub>("/groupChatHub");
app.MapHub<NotificationHub>("/notificationHub");
app.Run();
