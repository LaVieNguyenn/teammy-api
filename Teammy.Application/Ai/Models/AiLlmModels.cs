using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Teammy.Application.Ai.Models;

public sealed record AiLlmRerankRequest(
    string QueryType,
    string QueryText,
    IReadOnlyList<AiLlmCandidate> Candidates,
    IReadOnlyDictionary<string, string>? Context = null
);

public sealed record AiLlmCandidate(
    string Key,
    Guid Id,
    string Title,
    string? Description,
    string Payload,
    IReadOnlyDictionary<string, string>? Metadata = null
);

public sealed record AiLlmRerankResponse(
    [property: JsonPropertyName("ranked")] IReadOnlyList<AiLlmRerankedItem>? Items,
    [property: JsonPropertyName("error")] string? Error = null
);

public sealed record AiLlmRerankedItem(
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("finalScore")] double FinalScore,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("matchedSkills")] IReadOnlyList<string>? MatchedSkills,
    [property: JsonPropertyName("balanceNote")] string? BalanceNote
);

public sealed record AiSkillExtractionRequest(
    string SourceType,
    string SourceId,
    string Content
);

public sealed record AiSkillExtractionResponse(IReadOnlyList<AiSkillEvidence> Skills);

public sealed record AiSkillEvidence(
    string Name,
    double Confidence,
    string? Evidence
);
