using System.Text.Json;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Files;
using Teammy.Application.Users.Dtos;

namespace Teammy.Application.Users.Services;

public sealed class UserProfileService(
    IUserReadOnlyQueries read,
    IUserWriteRepository write,
    IFileStorage storage,
    IGroupRepository groupRepository)
{
    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var profile = await read.GetProfileAsync(userId, ct);
        return profile ?? throw new KeyNotFoundException("User not found");
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ArgumentException("DisplayName is required.", nameof(request.DisplayName));

        var current = await read.GetProfileAsync(userId, ct) ?? throw new KeyNotFoundException("User not found");

        var skillsJson = NormalizeJson(request.Skills);

        var normalizedGender = request.Gender is null
            ? null
            : request.Gender.Trim().ToLowerInvariant();

        await write.UpdateProfileAsync(
            userId,
            request.DisplayName.Trim(),
            request.Phone?.Trim(),
            current.StudentCode,
            normalizedGender,
            current.MajorId,
            skillsJson,
            request.SkillsCompleted,
            NormalizePortfolio(request.PortfolioUrl),
            ct);

        await groupRepository.RefreshSkillsForMemberAsync(userId, ct);

        return await GetProfileAsync(userId, ct);
    }

    public async Task<UserProfileDto> UpdateAvatarAsync(Guid userId, Stream content, string fileName, CancellationToken ct)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        var current = await read.GetProfileAsync(userId, ct) ?? throw new KeyNotFoundException("User not found");
        var (url, _, _) = await storage.SaveAsync(content, fileName, ct);
        await write.UpdateAvatarAsync(userId, url, ct);

        if (!string.IsNullOrWhiteSpace(current.AvatarUrl))
        {
            try { await storage.DeleteAsync(current.AvatarUrl, ct); }
            catch { /* ignore storage cleanup errors */ }
        }

        return await GetProfileAsync(userId, ct);
    }

    private static string? NormalizeJson(JsonElement? element)
    {
        if (!element.HasValue) return null;
        var value = element.Value;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return JsonSerializer.Serialize(value);
    }

    private static string? NormalizePortfolio(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
