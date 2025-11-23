using Teammy.Application.Chat.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Chat.Services;

public sealed class ChatConversationService(IChatRepository repo)
{
    public Task<IReadOnlyList<ConversationSummaryDto>> ListMyConversationsAsync(Guid currentUserId, CancellationToken ct)
        => repo.ListConversationsAsync(currentUserId, ct);

    public async Task<Guid> CreateDirectConversationAsync(Guid currentUserId, Guid otherUserId, CancellationToken ct)
    {
        if (currentUserId == otherUserId) throw new ArgumentException("Cannot create conversation with yourself");
        return await repo.EnsureDirectSessionAsync(currentUserId, otherUserId, ct);
    }
}
