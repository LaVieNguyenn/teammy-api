using Teammy.Application.Chat.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IChatRepository
{
    Task<Guid> EnsureGroupSessionAsync(Guid groupId, CancellationToken ct);
    Task<Guid> EnsureDirectSessionAsync(Guid userAId, Guid userBId, CancellationToken ct);
    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid chatSessionId, int limit, int offset, CancellationToken ct);
    Task<ChatMessageDto> AddMessageAsync(Guid chatSessionId, Guid senderUserId, string content, string? type, CancellationToken ct);
    Task UpdateMembersCountAsync(Guid chatSessionId, int members, CancellationToken ct);
    Task<IReadOnlyList<ConversationSummaryDto>> ListConversationsAsync(Guid userId, CancellationToken ct);
    Task<(string Type, Guid? GroupId)?> GetSessionInfoAsync(Guid chatSessionId, CancellationToken ct);
    Task<bool> IsParticipantAsync(Guid chatSessionId, Guid userId, CancellationToken ct);
}
