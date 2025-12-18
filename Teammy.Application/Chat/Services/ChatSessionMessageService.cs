using Teammy.Application.Chat.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Chat.Services;

public sealed class ChatSessionMessageService(IChatRepository repo, IGroupReadOnlyQueries groupQueries, IGroupChatNotifier notifier)
{
    private readonly IChatRepository _repo = repo;
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;
    private readonly IGroupChatNotifier _notifier = notifier;

    public async Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid sessionId, Guid currentUserId, int limit, int offset, CancellationToken ct)
    {
        var info = await _repo.GetSessionInfoAsync(sessionId, ct) ?? throw new KeyNotFoundException("Session not found");
        await EnsureAccessAsync(sessionId, info, currentUserId, ct);
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);
        return await _repo.ListMessagesAsync(sessionId, limit, offset, ct);
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid sessionId, Guid currentUserId, SendChatMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Content is required");

        var info = await _repo.GetSessionInfoAsync(sessionId, ct) ?? throw new KeyNotFoundException("Session not found");
        await EnsureAccessAsync(sessionId, info, currentUserId, ct);

        var message = await _repo.AddMessageAsync(sessionId, currentUserId, request.Content.Trim(), request.Type, ct);
        await NotifyAsync(info, sessionId, message, isUpdate: false, ct);
        return message;
    }

    public async Task<ChatMessageDto> SetPinAsync(Guid sessionId, Guid messageId, Guid currentUserId, bool pin, CancellationToken ct)
    {
        var info = await _repo.GetSessionInfoAsync(sessionId, ct) ?? throw new KeyNotFoundException("Session not found");
        await EnsureAccessAsync(sessionId, info, currentUserId, ct);

        var dto = await _repo.SetMessagePinAsync(sessionId, messageId, currentUserId, pin, ct);
        await NotifyAsync(info, sessionId, dto, isUpdate: true, ct);
        return dto;
    }

    public async Task<ChatMessageDto> DeleteMessageAsync(Guid sessionId, Guid messageId, Guid currentUserId, CancellationToken ct)
    {
        var info = await _repo.GetSessionInfoAsync(sessionId, ct) ?? throw new KeyNotFoundException("Session not found");
        await EnsureAccessAsync(sessionId, info, currentUserId, ct);

        var meta = await _repo.GetMessageMetaAsync(messageId, ct) ?? throw new KeyNotFoundException("Message not found");
        if (meta.ChatSessionId != sessionId)
            throw new InvalidOperationException("Message does not belong to this session");

        var canDelete = meta.SenderId == currentUserId;
        if (!canDelete && info.Type == "group" && info.GroupId.HasValue)
        {
            canDelete = await _groupQueries.IsLeaderAsync(info.GroupId.Value, currentUserId, ct);
        }
        if (!canDelete)
            throw new UnauthorizedAccessException("Cannot delete this message");

        var dto = await _repo.MarkMessageDeletedAsync(sessionId, messageId, currentUserId, ct);
        await NotifyAsync(info, sessionId, dto, isUpdate: true, ct);
        return dto;
    }

    private async Task EnsureAccessAsync(Guid sessionId, (string Type, Guid? GroupId) info, Guid currentUserId, CancellationToken ct)
    {
        if (info.Type == "group")
        {
            if (!info.GroupId.HasValue || !await _groupQueries.IsActiveMemberAsync(info.GroupId.Value, currentUserId, ct))
                throw new UnauthorizedAccessException("Members only");
        }
        else
        {
            var isParticipant = await _repo.IsParticipantAsync(sessionId, currentUserId, ct);
            if (!isParticipant)
                throw new UnauthorizedAccessException("Not part of this conversation");
        }
    }

    private Task NotifyAsync((string Type, Guid? GroupId) info, Guid sessionId, ChatMessageDto message, bool isUpdate, CancellationToken ct)
    {
        if (info.Type == "group" && info.GroupId.HasValue)
        {
            return isUpdate
                ? _notifier.NotifyMessageUpdatedAsync(info.GroupId, sessionId, message, ct)
                : _notifier.NotifyMessageAsync(info.GroupId.Value, message, ct);
        }

        return isUpdate
            ? _notifier.NotifyMessageUpdatedAsync(null, sessionId, message, ct)
            : _notifier.NotifySessionAsync(sessionId, message, ct);
    }
}
