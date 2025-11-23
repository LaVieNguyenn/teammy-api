using Teammy.Application.Chat.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupChatNotifier
{
    Task NotifyMessageAsync(Guid groupId, ChatMessageDto message, CancellationToken ct);
    Task NotifySessionAsync(Guid sessionId, ChatMessageDto message, CancellationToken ct);
}
