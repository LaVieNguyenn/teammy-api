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
        if (info.Type == "group" && info.GroupId.HasValue)
        {
            await _notifier.NotifyMessageAsync(info.GroupId.Value, message, ct);
        }
        else
        {
            await _notifier.NotifySessionAsync(sessionId, message, ct);
        }
        return message;
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
}
