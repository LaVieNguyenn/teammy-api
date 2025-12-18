using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Teammy.Infrastructure.Ai;

public sealed class AiGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public AiGatewayClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var baseUrl = configuration["AI_GATEWAY_BASE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("AI_GATEWAY_BASE_URL is not configured.");

        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        _httpClient.BaseAddress = new Uri(baseUrl);
        _apiKey = configuration["AI_GATEWAY_API_KEY"];
    }

    public async Task<AiGatewayHealthResult> GetHealthAsync(CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, "health");
        using var response = await _httpClient.SendAsync(request, ct);
        var payload = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(ct);

        return new AiGatewayHealthResult(response.IsSuccessStatusCode, (int)response.StatusCode, payload);
    }

    public async Task UpsertAsync(AiGatewayUpsertRequest req, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, "index/upsert");
        request.Content = JsonContent.Create(req);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string pointId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pointId))
            throw new ArgumentException("Point id is required.", nameof(pointId));

        using var request = CreateRequest(HttpMethod.Delete, $"index/delete/{pointId}");
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AiGatewaySearchResponse> SearchAsync(AiGatewaySearchRequest req, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, "search");
        request.Content = JsonContent.Create(req);
        using var response = await _httpClient.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        // Legacy gateway: { "result": [ { "score": 0.12, "payload": {"type":"","entityId":"","title":""} } ] }
        // Local gateway:  { "hits":   [ { "distance": 0.34, "payload": {"type":"","entityId":"","title":""} } ] }
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("result", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<AiGatewaySearchResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        if (!document.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return new AiGatewaySearchResponse(Array.Empty<AiGatewaySearchHit>());

        var normalized = new List<AiGatewaySearchHit>();
        foreach (var hit in hits.EnumerateArray())
        {
            double score = 0;
            if (hit.TryGetProperty("distance", out var distEl) && distEl.ValueKind == JsonValueKind.Number && distEl.TryGetDouble(out var distance))
            {
                // Convert distance (lower is better) into a monotonic score (higher is better)
                score = 1.0 / (1.0 + Math.Max(0, distance));
            }

            if (!hit.TryGetProperty("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.Object)
                continue;

            var type = payloadEl.TryGetProperty("type", out var t) ? t.GetString() ?? t.ToString() : string.Empty;
            var entityId = payloadEl.TryGetProperty("entityId", out var e) ? (e.GetString() ?? e.ToString()) : string.Empty;
            var title = payloadEl.TryGetProperty("title", out var ti) ? (ti.GetString() ?? ti.ToString()) : null;

            normalized.Add(new AiGatewaySearchHit(score, new AiGatewaySearchPayload(type, entityId, title)));
        }

        return new AiGatewaySearchResponse(normalized);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }
}

public sealed record AiGatewayHealthResult(bool IsSuccess, int StatusCode, string? Body);

public sealed record AiGatewayUpsertRequest(
    string Type,
    string EntityId,
    string? Title,
    string Text,
    string? SemesterId,
    string? MajorId,
    string? PointId);

public sealed record AiGatewaySearchRequest(
    string QueryText,
    string? Type,
    string? SemesterId,
    string? MajorId,
    int Limit,
    double? ScoreThreshold);

public sealed record AiGatewaySearchPayload(string type, string entityId, string? title);

public sealed record AiGatewaySearchHit(double score, AiGatewaySearchPayload payload);

public sealed record AiGatewaySearchResponse(IReadOnlyList<AiGatewaySearchHit> result);
