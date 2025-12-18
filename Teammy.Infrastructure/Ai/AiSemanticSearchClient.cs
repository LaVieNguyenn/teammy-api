using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Infrastructure.Ai;

public sealed class AiSemanticSearchClient(HttpClient httpClient, IConfiguration configuration) : IAiSemanticSearch
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly string _apiKey = configuration?["AI_GATEWAY_API_KEY"] ?? string.Empty;

    public async Task<IReadOnlyList<Guid>> SearchIdsAsync(
        string queryText,
        string type,
        Guid semesterId,
        Guid? majorId,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<Guid>();

        using var request = new HttpRequestMessage(HttpMethod.Post, "search");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            queryText,
            type,
            semesterId = semesterId.ToString(),
            majorId = majorId?.ToString(),
            limit
        };

        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Support both legacy gateway shape: {"result":[{"payload":{"entityId":"..."}}]}
        // and local gateway shape: {"hits":[{"payload":{"entityId":"..."},"distance":...}]}
        JsonElement resultElement;
        if (document.RootElement.TryGetProperty("result", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
        {
            resultElement = legacy;
        }
        else if (document.RootElement.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            resultElement = hits;
        }
        else
        {
            return Array.Empty<Guid>();
        }

        var ids = new List<Guid>();
        foreach (var item in resultElement.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
                continue;

            if (!payloadElement.TryGetProperty("entityId", out var entityElement))
                continue;

            var entityId = entityElement.ValueKind == JsonValueKind.String
                ? entityElement.GetString()
                : entityElement.ToString();
            if (Guid.TryParse(entityId, out var guid))
                ids.Add(guid);
        }

        return ids;
    }
}
