using Teammy.Application.Chat.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Chat.Services;

public sealed class GroupChatService(IChatRepository chatRepository, IGroupReadOnlyQueries groupQueries, IGroupChatNotifier notifier)
{
    private readonly IChatRepository _chatRepository = chatRepository;
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;
    private readonly IGroupChatNotifier _notifier = notifier;

    public async Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid groupId, Guid currentUserId, int limit, int offset, CancellationToken ct)
    {
        if (!await IsMemberOrMentorAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Members only");

        var sessionId = await EnsureSessionAndSyncMembersAsync(groupId, ct);
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);
        return await _chatRepository.ListMessagesAsync(sessionId, limit, offset, ct);
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid groupId, Guid currentUserId, SendChatMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Content is required");

        if (!await IsMemberOrMentorAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Members only");

        var sessionId = await EnsureSessionAndSyncMembersAsync(groupId, ct);
        var message = await _chatRepository.AddMessageAsync(sessionId, currentUserId, request.Content.Trim(), request.Type, ct);
        await _notifier.NotifyMessageAsync(groupId, message, ct);
        return message;
    }

    private async Task<Guid> EnsureSessionAndSyncMembersAsync(Guid groupId, CancellationToken ct)
    {
        var sessionId = await _chatRepository.EnsureGroupSessionAsync(groupId, ct);
        var (_, activeCount) = await _groupQueries.GetGroupCapacityAsync(groupId, ct);
        var hasMentor = await _groupQueries.GetMentorAsync(groupId, ct) is not null;
        await _chatRepository.UpdateMembersCountAsync(sessionId, hasMentor ? activeCount + 1 : activeCount, ct);
        return sessionId;
    }

    private async Task<bool> IsMemberOrMentorAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        if (await _groupQueries.IsActiveMemberAsync(groupId, userId, ct))
            return true;
        return await _groupQueries.IsMentorAsync(groupId, userId, ct);
    }
}
