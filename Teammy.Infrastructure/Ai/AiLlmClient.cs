using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Teammy.Application.Ai.Models;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Infrastructure.Ai;

public sealed class AiLlmClient : IAiLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public AiLlmClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = configuration?["AI_GATEWAY_API_KEY"] ?? string.Empty;
    }

    public async Task<AiLlmRerankResponse> RerankAsync(AiLlmRerankRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        // Local gateway expects (backward compatible):
        // { queryText, policy?, topN?, candidates:[{key, entityId, title, text}] }
        // Additionally, we may provide a richer shape for AI hosts:
        // { mode, topN, queryText, team:{...}, candidates:[{key, entityId, title, text, neededRole, baselineScore}] }
        var policy = request.Context is null
            ? null
            : (request.Context.TryGetValue("policy", out var p) ? p : null);

        var mode = request.Context is null
            ? null
            : (request.Context.TryGetValue("mode", out var m) ? m : null);

        // If caller didn't specify, infer from QueryType.
        // Gateway supports: topic | group_post | personal_post
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = request.QueryType switch
            {
                "recruitment_post" => "group_post",
                "profile_post" => "personal_post",
                "topic" => "topic",
                "topic_options" => "topic",
                _ => null
            };
        }

        int? topN = null;
        if (request.Context is not null && request.Context.TryGetValue("topN", out var topNRaw)
            && int.TryParse(topNRaw, out var topNValue) && topNValue > 0)
        {
            topN = topNValue;
        }

        bool? withReasons = null;
        if (request.Context is not null && request.Context.TryGetValue("withReasons", out var wrRaw)
            && !string.IsNullOrWhiteSpace(wrRaw) && bool.TryParse(wrRaw, out var wr))
        {
            withReasons = wr;
        }

        JsonElement? team = null;
        if (request.Context is not null && request.Context.TryGetValue("team", out var teamJson)
            && !string.IsNullOrWhiteSpace(teamJson))
        {
            try
            {
                team = JsonSerializer.Deserialize<JsonElement>(teamJson, SerializerOptions);
            }
            catch
            {
                team = null;
            }
        }

        var candidatePayload = request.Candidates.Select(c =>
        {
            var neededRole = c.Metadata is null
                ? null
                : (c.Metadata.TryGetValue("neededRole", out var nr) ? nr
                    : (c.Metadata.TryGetValue("position", out var pos) ? pos : null));

            // ai-gateway expects baselineScore as int (it parses with GetIntAny)
            int? baselineScore = null;
            if (c.Metadata is not null && c.Metadata.TryGetValue("score", out var scoreRaw)
                && double.TryParse(scoreRaw, out var scoreValue))
            {
                baselineScore = (int)Math.Round(scoreValue, MidpointRounding.AwayFromZero);
            }

            return new
            {
                key = c.Key,
                entityId = c.Id.ToString(),
                title = c.Title,
                text = string.IsNullOrWhiteSpace(c.Payload) ? (c.Description ?? c.Title) : c.Payload,
                baselineScore,
                // Also include metadata so the gateway can read metadata.score (and future fields) if needed.
                metadata = c.Metadata
            };
        }).ToList();

        object upstreamPayload;
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedMode is "group_post" or "personal_post" or "auto_assign_team")
        {
            // Post modes: include team (optional) + neededRole.
            var postCandidates = request.Candidates.Select((c, i) =>
            {
                var neededRole = c.Metadata is null
                    ? null
                    : (c.Metadata.TryGetValue("neededRole", out var nr) ? nr
                        : (c.Metadata.TryGetValue("position", out var pos) ? pos : null));

                int? baselineScore = null;
                if (c.Metadata is not null && c.Metadata.TryGetValue("score", out var scoreRaw)
                    && double.TryParse(scoreRaw, out var scoreValue))
                {
                    baselineScore = (int)Math.Round(scoreValue, MidpointRounding.AwayFromZero);
                }

                return new
                {
                    key = string.IsNullOrWhiteSpace(c.Key) ? $"c{i + 1:00}" : c.Key,
                    entityId = c.Id.ToString(),
                    title = c.Title,
                    text = string.IsNullOrWhiteSpace(c.Payload) ? (c.Description ?? c.Title) : c.Payload,
                    neededRole,
                    baselineScore,
                    metadata = c.Metadata
                };
            }).ToList();

            upstreamPayload = new
            {
                mode = normalizedMode,
                topN,
                withReasons,
                queryText = request.QueryText,
                policy,
                team,
                candidates = postCandidates
            };
        }
        else
        {
            // Topic mode: do NOT send team/neededRole so gateway uses topic prompt style.
            upstreamPayload = new
            {
                mode = string.IsNullOrWhiteSpace(normalizedMode) ? "topic" : normalizedMode,
                topN,
                withReasons,
                queryText = request.QueryText,
                policy,
                candidates = candidatePayload
            };
        }

        using var httpRequest = CreateRequest("llm/rerank", upstreamPayload);
        using var response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var responsePayload = await response.Content.ReadFromJsonAsync<AiLlmRerankResponse>(SerializerOptions, ct);
        return responsePayload ?? new AiLlmRerankResponse(Array.Empty<AiLlmRerankedItem>());
    }

    public async Task<AiSkillExtractionResponse> ExtractSkillsAsync(AiSkillExtractionRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        // Local gateway expects: { text, knownSkills?, maxSkills }
        var upstreamRequest = new
        {
            text = request.Content,
            knownSkills = (IReadOnlyList<string>?)null,
            maxSkills = 40
        };

        using var httpRequest = CreateRequest("llm/extract-skills", upstreamRequest);
        using var response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Expected local response:
        // {"primaryRole":"...", "skills":["C#","SQL"], "evidence":[{"skill":"C#","quote":"..."}]}
        if (!document.RootElement.TryGetProperty("skills", out var skillsEl) || skillsEl.ValueKind != JsonValueKind.Array)
            return new AiSkillExtractionResponse(Array.Empty<AiSkillEvidence>());

        var evidenceBySkill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("evidence", out var evidenceEl) && evidenceEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ev in evidenceEl.EnumerateArray())
            {
                if (ev.ValueKind != JsonValueKind.Object)
                    continue;

                var skill = ev.TryGetProperty("skill", out var s) ? (s.GetString() ?? s.ToString()) : null;
                var quote = ev.TryGetProperty("quote", out var q) ? (q.GetString() ?? q.ToString()) : null;
                if (string.IsNullOrWhiteSpace(skill) || string.IsNullOrWhiteSpace(quote))
                    continue;

                if (!evidenceBySkill.ContainsKey(skill))
                    evidenceBySkill[skill] = quote;
            }
        }

        var results = new List<AiSkillEvidence>();
        foreach (var skillEl in skillsEl.EnumerateArray())
        {
            var name = skillEl.ValueKind == JsonValueKind.String ? skillEl.GetString() : skillEl.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            evidenceBySkill.TryGetValue(name.Trim(), out var quote);
            results.Add(new AiSkillEvidence(name.Trim(), 1.0, quote));
        }

        return new AiSkillExtractionResponse(results);
    }

    private HttpRequestMessage CreateRequest<T>(string path, T payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        return request;
    }
}
