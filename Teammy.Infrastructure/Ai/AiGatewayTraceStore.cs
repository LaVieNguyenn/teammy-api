using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Infrastructure.Ai;

public sealed class AiGatewayTraceStore : IAiGatewayTraceStore
{
    private readonly ConcurrentQueue<AiGatewayTraceEntry> _entries = new();
    private readonly int _maxEntries;

    public AiGatewayTraceStore(int maxEntries = 50)
    {
        _maxEntries = Math.Clamp(maxEntries, 5, 500);
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    public void Add(AiGatewayTraceEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _maxEntries && _entries.TryDequeue(out _)) { }
    }

    public IReadOnlyList<AiGatewayTraceEntry> GetRecent(int take = 20)
    {
        take = Math.Clamp(take, 1, 200);
        return _entries.Reverse().Take(take).ToArray();
    }
}
