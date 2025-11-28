using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Teammy.Application.Ai.Models;

public sealed record AiSkillProfile(AiPrimaryRole PrimaryRole, IReadOnlyCollection<string> Tags)
{
    public static AiSkillProfile Empty { get; } = new(AiPrimaryRole.Unknown, Array.Empty<string>());

    public bool HasTags => Tags.Count > 0;

    public static AiSkillProfile FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Object => BuildFromObject(doc.RootElement),
                JsonValueKind.Array => BuildFromArray(doc.RootElement),
                JsonValueKind.String => FromText(doc.RootElement.GetString()),
                _ => Empty
            };
        }
        catch
        {
            return Empty;
        }
    }

    public static AiSkillProfile FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        var tags = SplitTerms(text);
        var role = InferRoleFromTokens(tags);
        return new AiSkillProfile(role, tags);
    }

    public static AiSkillProfile Combine(IEnumerable<AiSkillProfile> profiles)
    {
        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleCounter = new Dictionary<AiPrimaryRole, int>();

        foreach (var profile in profiles)
        {
            foreach (var tag in profile.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    tagSet.Add(tag);
            }

            if (profile.PrimaryRole != AiPrimaryRole.Unknown)
            {
                roleCounter.TryGetValue(profile.PrimaryRole, out var current);
                roleCounter[profile.PrimaryRole] = current + 1;
            }
        }

        var dominantRole = roleCounter
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .FirstOrDefault();

        if (dominantRole == default)
            dominantRole = AiPrimaryRole.Unknown;

        return new AiSkillProfile(dominantRole, tagSet.ToList());
    }

    public IReadOnlyList<string> FindMatches(AiSkillProfile other)
    {
        if (!HasTags || !other.HasTags)
            return Array.Empty<string>();

        var set = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase);
        return other.Tags
            .Where(tag => set.Contains(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public HashSet<string> ToSet() => new(Tags, StringComparer.OrdinalIgnoreCase);

    private static string? ExtractRole(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("primary_role", out var snake) && snake.ValueKind == JsonValueKind.String)
            return snake.GetString();
        if (root.TryGetProperty("primaryRole", out var camel) && camel.ValueKind == JsonValueKind.String)
            return camel.GetString();
        if (root.TryGetProperty("primary", out var shortProp) && shortProp.ValueKind == JsonValueKind.String)
            return shortProp.GetString();
        return null;
    }

    private static IReadOnlyCollection<string> ExtractTags(JsonElement root)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object)
            return tags;

        AddTags(root, "skill_tags", tags);
        AddTags(root, "skillTags", tags);
        AddTags(root, "skills", tags);
        AddTags(root, "tags", tags);
        AddTags(root, "stack", tags);

        return tags;
    }

    private static AiSkillProfile BuildFromObject(JsonElement root)
    {
        var role = ExtractRole(root);
        var tags = ExtractTags(root);
        return new AiSkillProfile(AiRoleHelper.Parse(role), tags);
    }

    private static AiSkillProfile BuildFromArray(JsonElement array)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTags(array, tags);
        return new AiSkillProfile(AiPrimaryRole.Unknown, tags);
    }

    private static void AddTags(JsonElement root, string propertyName, HashSet<string> tags)
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return;

        AddTags(element, tags);
    }

    private static void AddTags(JsonElement element, HashSet<string> tags)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        AddTag(item.GetString(), tags);
                }
                break;
            case JsonValueKind.String:
                foreach (var token in SplitTerms(element.GetString()))
                    tags.Add(token);
                break;
        }
    }

    private static IReadOnlyCollection<string> SplitTerms(string? value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return result;

        var primarySeparators = new[] { ',', ';', '/', '|', '\n', '\r', '\t' };
        var chunks = value.Split(primarySeparators, StringSplitOptions.RemoveEmptyEntries);
        if (chunks.Length == 0)
            chunks = new[] { value };

        foreach (var chunk in chunks)
        {
            var tokens = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
                AddTag(token, result);
        }

        return result;
    }

    private static void AddTag(string? raw, HashSet<string> tags)
    {
        var normalized = Normalize(raw);
        if (!string.IsNullOrEmpty(normalized))
            tags.Add(normalized);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().ToLowerInvariant();
    }

    private static AiPrimaryRole InferRoleFromTokens(IReadOnlyCollection<string> tags)
    {
        var tokenSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        if (tokenSet.Any(t => t.Contains("front") || t.Contains("ui") || t.Contains("ux") || t.Contains("react") || t.Contains("design")))
            return AiPrimaryRole.Frontend;
        if (tokenSet.Any(t => t.Contains("back") || t.Contains("api") || t.Contains("server") || t.Contains("database") || t.Contains("python") || t.Contains("etl")))
            return AiPrimaryRole.Backend;
        return AiPrimaryRole.Other;
    }
}
