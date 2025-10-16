// Teammy.Infrastructure/Auth/FirebaseTokenVerifier.cs
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Teammy.Application.Common.Interfaces.Auth;
using System.Text.Json;

namespace Teammy.Infrastructure.Auth;

public sealed class FirebaseTokenVerifier : IExternalTokenVerifier
{
    private static bool _inited;

    public FirebaseTokenVerifier(IConfiguration cfg, IHostEnvironment env, ILogger<FirebaseTokenVerifier> logger)
    {
        if (_inited) return;

        GoogleCredential credential;
        var saPath = cfg["Auth:Firebase:ServiceAccountPath"];

        if (!string.IsNullOrWhiteSpace(saPath))
        {
            // resolve relative -> ContentRoot (thư mục chứa .csproj khi dotnet run)
            if (!Path.IsPathRooted(saPath))
                saPath = Path.Combine(env.ContentRootPath, saPath);

            logger.LogInformation("Firebase: using ServiceAccountPath = {Path}", saPath);

            if (!File.Exists(saPath))
                throw new FileNotFoundException($"Service account file not found: {saPath}");

            credential = GoogleCredential.FromFile(saPath);
        }
        else if (cfg.GetSection("Auth:Firebase:ServiceAccountJson").Exists()
                 && cfg.GetSection("Auth:Firebase:ServiceAccountJson").Value is null)
        {
            // nếu bạn vẫn để object JSON trong appsettings
            var raw = cfg.GetSection("Auth:Firebase:ServiceAccountJson").Get<JsonElement>().GetRawText();
            logger.LogInformation("Firebase: using ServiceAccountJson from configuration.");
            credential = GoogleCredential.FromJson(raw);
        }
        else if (!string.IsNullOrWhiteSpace(cfg["Auth:Firebase:ServiceAccountJson"]))
        {
            // JSON dạng string
            logger.LogInformation("Firebase: using ServiceAccountJson (string) from configuration.");
            credential = GoogleCredential.FromJson(cfg["Auth:Firebase:ServiceAccountJson"]!);
        }
        else
        {
            logger.LogWarning("Firebase: no ServiceAccount configured, falling back to Application Default Credentials (ADC).");
            credential = GoogleCredential.GetApplicationDefault();
        }

        FirebaseApp.Create(new AppOptions { Credential = credential });
        _inited = true;
    }

    public async Task<ExternalUserInfo> VerifyAsync(string idToken, CancellationToken ct)
    {
        var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken, ct);

        var sub  = decoded.Uid;
        var ok   = decoded.Claims.TryGetValue("email_verified", out var v) && v is bool b && b;
        var mail = decoded.Claims.TryGetValue("email", out var e) ? e?.ToString() : null;
        var name = decoded.Claims.TryGetValue("name", out var n) ? n?.ToString() : mail ?? "User";
        var pic  = decoded.Claims.TryGetValue("picture", out var p) ? p?.ToString() : null;

        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(mail))
            throw new UnauthorizedAccessException("Token missing sub/email.");

        return new ExternalUserInfo(sub, mail!, ok, name!, pic);
    }
}
