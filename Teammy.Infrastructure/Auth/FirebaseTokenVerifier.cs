using Google.Apis.Auth.OAuth2;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Teammy.Application.Common.Interfaces;
using System.Text;

namespace Teammy.Infrastructure.Auth;

public sealed class FirebaseTokenVerifier : IExternalTokenVerifier
{
    private readonly FirebaseApp _app;

    public FirebaseTokenVerifier(IConfiguration cfg, IHostEnvironment env)
    {
        var firebaseJson = Environment.GetEnvironmentVariable("FirebasePath");

        if (!string.IsNullOrEmpty(firebaseJson))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(firebaseJson));
            _app = FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromStream(stream)
            }, $"teammy-{Guid.NewGuid()}");
        }
        else
        {
            var relPath = cfg["Auth:Firebase:ServiceAccountPath"]
                ?? throw new InvalidOperationException("Auth:Firebase:ServiceAccountPath is required");
            var fullPath = Path.Combine(env.ContentRootPath, relPath);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Firebase credential not found", fullPath);

            _app = FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromFile(fullPath)
            }, $"teammy-{Guid.NewGuid()}");
        }
    }

    public async Task<ExternalUserInfo> VerifyAsync(string idToken, CancellationToken ct)
    {
        var decoded = await FirebaseAuth.GetAuth(_app).VerifyIdTokenAsync(idToken, ct);

        decoded.Claims.TryGetValue("email", out var emailObj);
        decoded.Claims.TryGetValue("email_verified", out var evObj);
        decoded.Claims.TryGetValue("name", out var nameObj);
        decoded.Claims.TryGetValue("picture", out var picObj);

        var email = emailObj?.ToString() ?? throw new InvalidOperationException("No email in Firebase token");
        var verified = evObj is bool b && b;

        return new ExternalUserInfo(email, verified, nameObj?.ToString(), picObj?.ToString());
    }
}
