using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using Teammy.Application.Files;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Teammy.Infrastructure.Files;

public sealed class GoogleDriveStorage : IFileStorage
{
    private readonly DriveService _drive;
    private readonly string _rootFolderId;
    private readonly bool _supportsAllDrives;
    private readonly bool _makePublicLink;
    private readonly ILogger<GoogleDriveStorage> _logger;

    public GoogleDriveStorage(IConfiguration cfg, IHostEnvironment _env, ILogger<GoogleDriveStorage> logger)
    {
        _logger = logger;

        var section = cfg.GetSection("Storage:GoogleDrive");
        var authMode = (section["AuthMode"] ?? "UserOAuth").Trim(); // "UserOAuth" | "ServiceAccount"

        _rootFolderId = section["FolderId"]
            ?? throw new InvalidOperationException("Missing Storage:GoogleDrive:FolderId");

        _makePublicLink = bool.TryParse(section["OAuth:PublicLink"], out var pl) ? pl : true;

        GoogleCredential credential;

        if (authMode.Equals("UserOAuth", StringComparison.OrdinalIgnoreCase))
        {
            // ===== User OAuth (Gmail cá nhân) =====
            var clientId = section["OAuth:ClientId"]
                ?? throw new InvalidOperationException("Missing Storage:GoogleDrive:OAuth:ClientId");

            var clientSecret = section["OAuth:ClientSecret"]
                ?? throw new InvalidOperationException("Missing Storage:GoogleDrive:OAuth:ClientSecret");

            var refreshToken = section["OAuth:RefreshToken"]
                ?? throw new InvalidOperationException("Missing Storage:GoogleDrive:OAuth:RefreshToken");

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = new[] { DriveService.Scope.Drive } 
            });

            var token = new TokenResponse { RefreshToken = refreshToken };

            var userCred = new UserCredential(flow, "teammy-user", token);

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = userCred,
                ApplicationName = "Teammy"
            });

            _supportsAllDrives = false; 
        }
        else
        {
            // ===== Service Account (Shared Drive / Workspace) =====
            var json = section["ServiceAccountJson"]
                ?? throw new InvalidOperationException("Missing Storage:GoogleDrive:ServiceAccountJson");

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            credential = GoogleCredential.FromStream(ms)
                .CreateScoped(DriveService.Scope.Drive);

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Teammy"
            });

            _supportsAllDrives = true; 
        }

        // ===== Validate FolderId =====
        try
        {
            var get = _drive.Files.Get(_rootFolderId);
            get.Fields = "id,name,mimeType,driveId";
            get.SupportsAllDrives = _supportsAllDrives;
            var folder = get.Execute();

            if (folder.MimeType != "application/vnd.google-apps.folder")
                throw new InvalidOperationException($"FolderId '{_rootFolderId}' is not a folder.");

            if (_supportsAllDrives && string.IsNullOrEmpty(folder.DriveId))
            {
                throw new InvalidOperationException(
                    "ServiceAccount mode requires a folder INSIDE a Shared Drive (driveId is empty).");
            }
        }
        catch (Google.GoogleApiException gex)
        {
            throw new InvalidOperationException(
                $"Cannot access FolderId '{_rootFolderId}'. Google says: {gex.Error?.Message}", gex);
        }
    }

    public async Task<(string fileUrl, string? fileType, long? fileSize)> SaveAsync(
        Stream content, string fileName, CancellationToken ct)
    {
        var (dirPath, safeName, contentType) = SanitizePathAndName(fileName);
        if (content.CanSeek) content.Position = 0;

        var parentId = await EnsureFolderChainAsync(_rootFolderId, dirPath, ct);

        var meta = new DriveFile { Name = safeName, Parents = new[] { parentId } };
        var create = _drive.Files.Create(meta, content, contentType);
        create.Fields = "id,mimeType,size,name,parents";
        create.SupportsAllDrives = _supportsAllDrives;

        IUploadProgress prog = await create.UploadAsync(ct);
        if (prog.Status != UploadStatus.Completed)
            throw new InvalidOperationException(
                $"Google Drive upload failed: {prog.Status}. {prog.Exception?.Message}", prog.Exception);

        var f = create.ResponseBody ?? throw new InvalidOperationException("Upload ok but no response body.");

        if (_makePublicLink)
        {
            try
            {
                var perm = new Permission { Type = "anyone", Role = "reader" };
                var req = _drive.Permissions.Create(perm, f.Id);
                req.SupportsAllDrives = _supportsAllDrives;
                await req.ExecuteAsync(ct);
            }
            catch (Google.GoogleApiException gex)
            {
                _logger.LogWarning("Grant public read failed: {Message}", gex.Message);
            }
        }

        var url = BuildDirectDownloadUrl(f.Id);
        return (url, f.MimeType, f.Size);
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken ct)
    {
        var id = TryExtractFileId(fileUrl);
        if (string.IsNullOrEmpty(id)) return;

        try
        {
            var del = _drive.Files.Delete(id);
            del.SupportsAllDrives = _supportsAllDrives;
            await del.ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete failed for {Url}", fileUrl);
        }
    }

    // ===== Helpers =====

    private static (string? dirPath, string safeName, string contentType) SanitizePathAndName(string input)
    {
        var normalized = input.Replace("\\", "/");
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                              .Select(SanitizeSegment).ToArray();
        string? dir = parts.Length > 1 ? string.Join('/', parts[..^1]) : null;
        var ext = Path.GetExtension(parts.LastOrDefault() ?? string.Empty)?.ToLowerInvariant();
        var safeName = $"{Guid.NewGuid():N}{ext}";
        var mime = GetMime(ext);
        return (dir, safeName, mime);

        static string SanitizeSegment(string s)
        {
            var clean = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ' ').ToArray());
            return string.IsNullOrWhiteSpace(clean) ? "x" : clean;
        }
    }

    private static string GetMime(string? ext) => ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    private async Task<string> EnsureFolderChainAsync(string rootId, string? dirPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dirPath)) return rootId;

        var parent = rootId;
        foreach (var seg in dirPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var list = _drive.Files.List();
            list.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{seg.Replace("'", "\\'")}' and '{parent}' in parents and trashed = false";
            list.Fields = "files(id,name)";
            list.SupportsAllDrives = _supportsAllDrives;
            list.IncludeItemsFromAllDrives = _supportsAllDrives;

            var resp = await list.ExecuteAsync(ct);
            var found = resp.Files?.FirstOrDefault();
            if (found is not null) { parent = found.Id; continue; }

            var folderMeta = new DriveFile
            {
                Name = seg,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new[] { parent }
            };
            var create = _drive.Files.Create(folderMeta);
            create.Fields = "id";
            create.SupportsAllDrives = _supportsAllDrives;
            var created = await create.ExecuteAsync(ct);
            parent = created.Id;
        }
        return parent;
    }

    private static string BuildDirectDownloadUrl(string fileId)
        => $"https://drive.google.com/uc?id={fileId}&export=download";

    private static string? TryExtractFileId(string url)
    {
        try
        {
            var uri = new Uri(url);
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var byId = q.Get("id");
            if (!string.IsNullOrWhiteSpace(byId)) return byId;

            var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var idx = Array.IndexOf(segs, "d"); // /file/d/{id}/view
            if (idx >= 0 && idx + 1 < segs.Length) return segs[idx + 1];
        }
        catch { }
        return null;
    }
}
