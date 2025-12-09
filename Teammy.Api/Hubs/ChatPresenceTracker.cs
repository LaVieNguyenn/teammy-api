using System.Collections.Concurrent;
using System.Linq;

namespace Teammy.Api.Hubs;

public interface IChatPresenceTracker
{
    IReadOnlyList<ChatPresenceUser> AddSessionConnection(Guid sessionId, Guid userId, string? displayName, string connectionId);
    bool TryRemoveSessionConnection(Guid sessionId, Guid userId, string connectionId, out string? displayName, out bool userRemoved);
    IReadOnlyList<(Guid SessionId, Guid UserId, string? DisplayName)> RemoveConnection(string connectionId);
}

public sealed record ChatPresenceUser(Guid UserId, string? DisplayName);

public sealed class ChatPresenceTracker : IChatPresenceTracker
{
    private sealed class PresenceEntry
    {
        public PresenceEntry(Guid userId, string? displayName)
        {
            UserId = userId;
            DisplayName = displayName;
        }

        public Guid UserId { get; }
        public string? DisplayName { get; private set; }
        public HashSet<string> Connections { get; } = new();

        public void UpdateDisplayName(string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                DisplayName = displayName;
            }
        }
    }

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, PresenceEntry>> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<(Guid SessionId, Guid UserId), byte>> _connectionIndex = new();

    public IReadOnlyList<ChatPresenceUser> AddSessionConnection(Guid sessionId, Guid userId, string? displayName, string connectionId)
    {
        var session = _sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, PresenceEntry>());
        var entry = session.GetOrAdd(userId, _ => new PresenceEntry(userId, displayName));
        entry.UpdateDisplayName(displayName);

        lock (entry.Connections)
        {
            entry.Connections.Add(connectionId);
        }

        var connectionSessions = _connectionIndex.GetOrAdd(connectionId, _ => new ConcurrentDictionary<(Guid, Guid), byte>());
        connectionSessions.TryAdd((sessionId, userId), 0);

        return session.Values
            .Select(e => new ChatPresenceUser(e.UserId, e.DisplayName))
            .ToList();
    }

    public bool TryRemoveSessionConnection(Guid sessionId, Guid userId, string connectionId, out string? displayName, out bool userRemoved)
        => RemoveSessionConnectionInternal(sessionId, userId, connectionId, updateConnectionIndex: true, out displayName, out userRemoved);

    public IReadOnlyList<(Guid SessionId, Guid UserId, string? DisplayName)> RemoveConnection(string connectionId)
    {
        var removed = new List<(Guid SessionId, Guid UserId, string? DisplayName)>();
        if (!_connectionIndex.TryRemove(connectionId, out var map))
        {
            return removed;
        }

        foreach (var key in map.Keys)
        {
            if (RemoveSessionConnectionInternal(key.SessionId, key.UserId, connectionId, updateConnectionIndex: false, out var displayName, out var userRemoved)
                && userRemoved)
            {
                removed.Add((key.SessionId, key.UserId, displayName));
            }
        }

        return removed;
    }

    private bool RemoveSessionConnectionInternal(Guid sessionId, Guid userId, string connectionId, bool updateConnectionIndex, out string? displayName, out bool userRemoved)
    {
        displayName = null;
        userRemoved = false;

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        if (!session.TryGetValue(userId, out var entry))
        {
            return false;
        }

        displayName = entry.DisplayName;

        lock (entry.Connections)
        {
            if (!entry.Connections.Remove(connectionId))
            {
                return false;
            }

            if (entry.Connections.Count == 0)
            {
                session.TryRemove(userId, out _);
                userRemoved = true;
            }
        }

        if (session.IsEmpty)
        {
            _sessions.TryRemove(sessionId, out _);
        }

        if (updateConnectionIndex && _connectionIndex.TryGetValue(connectionId, out var map))
        {
            map.TryRemove((sessionId, userId), out _);
            if (map.IsEmpty)
            {
                _connectionIndex.TryRemove(connectionId, out _);
            }
        }

        return true;
    }
}
