using System;
using System.Collections.Generic;

namespace Teammy.Application.Common.Interfaces;

public sealed record AiGatewayTraceEntry(
    DateTime AtUtc,
    string Operation,
    string? Mode,
    string? QueryType,
    string RequestJson,
    int? StatusCode,
    string? ResponseJson,
    long ElapsedMs);

public interface IAiGatewayTraceStore
{
    void Clear();
    void Add(AiGatewayTraceEntry entry);
    IReadOnlyList<AiGatewayTraceEntry> GetRecent(int take = 20);
}
