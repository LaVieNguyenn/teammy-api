using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Teammy.Api.Services;

public sealed class AiGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public AiGatewayClient(HttpClient httpClient, IConfiguration configuration)
    {
        if (httpClient is null)
            throw new ArgumentNullException(nameof(httpClient));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var baseUrl = configuration["AI_GATEWAY_BASE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("AI_GATEWAY_BASE_URL is not configured.");

        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient = httpClient;
        _apiKey = configuration["AI_GATEWAY_API_KEY"];
    }

    public async Task<AiGatewayHealthResult> GetHealthAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "health");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        var payload = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(ct);

        return new AiGatewayHealthResult(response.IsSuccessStatusCode, (int)response.StatusCode, payload);
    }
    public async Task UpsertAsync(AiGatewayUpsertRequest req, CancellationToken ct)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "index/upsert");
    if (!string.IsNullOrWhiteSpace(_apiKey))
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

    request.Content = JsonContent.Create(req);
    using var response = await _httpClient.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
}

public async Task<AiGatewaySearchResponse> SearchAsync(AiGatewaySearchRequest req, CancellationToken ct)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "search");
    if (!string.IsNullOrWhiteSpace(_apiKey))
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

    request.Content = JsonContent.Create(req);
    using var response = await _httpClient.SendAsync(request, ct);
    var json = await response.Content.ReadAsStringAsync(ct);
    response.EnsureSuccessStatusCode();
    return JsonSerializer.Deserialize<AiGatewaySearchResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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

public sealed record AiGatewaySearchHit(double score, AiGatewaySearchPayload payload);
public sealed record AiGatewaySearchPayload(string type, string entityId, string? title);

public sealed record AiGatewaySearchResponse(IReadOnlyList<AiGatewaySearchHit> result);
