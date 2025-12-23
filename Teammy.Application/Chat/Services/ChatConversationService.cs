using Teammy.Application.Chat.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Chat.Services;

public sealed class ChatConversationService(IChatRepository repo, IGroupReadOnlyQueries groupQueries)
{
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;

    public Task<IReadOnlyList<ConversationSummaryDto>> ListMyConversationsAsync(Guid currentUserId, CancellationToken ct)
        => repo.ListConversationsAsync(currentUserId, ct);

    public async Task<Guid> CreateDirectConversationAsync(Guid currentUserId, Guid otherUserId, CancellationToken ct)
    {
        if (currentUserId == otherUserId) throw new ArgumentException("Cannot create conversation with yourself");
        return await repo.EnsureDirectSessionAsync(currentUserId, otherUserId, ct);
    }

    public async Task SetPinAsync(Guid sessionId, Guid currentUserId, bool pin, CancellationToken ct)
    {
        var info = await repo.GetSessionInfoAsync(sessionId, ct) ?? throw new KeyNotFoundException("Session not found");
        if (info.Type == "group")
        {
            if (!info.GroupId.HasValue || !await _groupQueries.IsActiveMemberAsync(info.GroupId.Value, currentUserId, ct))
                throw new UnauthorizedAccessException("Members only");
        }
        else
        {
            if (!await repo.IsParticipantAsync(sessionId, currentUserId, ct))
                throw new UnauthorizedAccessException("Not part of this conversation");
        }

        await repo.SetSessionPinAsync(sessionId, pin, ct);
    }
}
