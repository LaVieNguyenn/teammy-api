// Teammy.Infrastructure/Files/GoogleDriveStorage.cs
using Google.Apis.Auth.OAuth2;
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
    private readonly ILogger<GoogleDriveStorage> _logger;

    public GoogleDriveStorage(IConfiguration cfg, IHostEnvironment env, ILogger<GoogleDriveStorage> logger)
    {
        _logger = logger;

        _rootFolderId = cfg["Storage:GoogleDrive:FolderId"]
            ?? throw new InvalidOperationException("Missing Storage:GoogleDrive:FolderId");

        var rawJson = Environment.GetEnvironmentVariable("GoogleDrivePath");

        GoogleCredential cred;
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(rawJson));
            cred = GoogleCredential.FromStream(ms);
        }
        else
        {
            var relPath = cfg["Storage:GoogleDrive:ServiceAccountPath"]
                ?? throw new InvalidOperationException("Provide env GoogleDrivePath or Storage:GoogleDrive:ServiceAccountPath");
            var fullPath = Path.Combine(env.ContentRootPath, relPath);
            if (!System.IO.File.Exists(fullPath))
                throw new FileNotFoundException("Service account json not found", fullPath);

            cred = GoogleCredential.FromFile(fullPath);
        }

        cred = cred.CreateScoped(DriveService.Scope.Drive);

        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = "Teammy"
        });

        // Kiểm tra quyền truy cập FolderId (fail sớm, báo lỗi rõ)
        try
        {
            var get = _drive.Files.Get(_rootFolderId);
            get.Fields = "id,name,mimeType";
            var folder = get.Execute();
            if (folder.MimeType != "application/vnd.google-apps.folder")
                throw new InvalidOperationException($"Storage:GoogleDrive:FolderId '{_rootFolderId}' is not a folder.");
        }
        catch (Google.GoogleApiException gex)
        {
            throw new InvalidOperationException(
                $"Cannot access Google Drive FolderId '{_rootFolderId}'. " +
                $"Make sure you ENABLED Drive API and SHARED this folder with the Service Account (Editor). " +
                $"Google says: {gex.Error?.Message}", gex);
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

        IUploadProgress prog = await create.UploadAsync(ct);
        if (prog.Status != UploadStatus.Completed)
            throw new InvalidOperationException($"Google Drive upload failed: {prog.Status}. {prog.Exception?.Message}", prog.Exception);

        var f = create.ResponseBody ?? throw new InvalidOperationException("Upload ok but no response body.");
        // Cho phép xem/tải bằng link (best-effort)
        try
        {
            var perm = new Permission { Type = "anyone", Role = "reader" };
            await _drive.Permissions.Create(perm, f.Id).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException gex)
        {
            _logger.LogWarning("Grant public read failed: {Message}", gex.Message);
        }

        var url = BuildDirectDownloadUrl(f.Id);
        return (url, f.MimeType, f.Size);
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken ct)
    {
        var id = TryExtractFileId(fileUrl);
        if (string.IsNullOrEmpty(id)) return;
        try { await _drive.Files.Delete(id).ExecuteAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Delete failed for {Url}", fileUrl); }
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
            var resp = await list.ExecuteAsync(ct);

            var found = resp.Files?.FirstOrDefault();
            if (found is not null) { parent = found.Id; continue; }

            var folderMeta = new DriveFile
            {
                Name = seg,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new[] { parent }
            };
            var created = await _drive.Files.Create(folderMeta).ExecuteAsync(ct);
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
