namespace Teammy.Application.Chat.Dtos;

public sealed record ChatMessageDto(
    Guid MessageId,
    Guid SenderId,
    string SenderDisplayName,
    string SenderEmail,
    string? SenderAvatarUrl,
    string? Type,
    string Content,
    DateTime CreatedAt,
    bool IsPinned,
    Guid? PinnedBy,
    DateTime? PinnedAt,
    bool IsDeleted,
    Guid? DeletedBy,
    DateTime? DeletedAt);

public sealed record ConversationSummaryDto(
    Guid SessionId,
    string Type,
    Guid? GroupId,
    string? GroupName,
    Guid? OtherUserId,
    string? OtherDisplayName,
    string? OtherAvatarUrl,
    string? LastMessage,
    DateTime UpdatedAt,
    int UnreadCount,
    bool IsPinned,
    DateTime? PinnedAt);

public sealed class SendChatMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Type { get; set; }
}

public sealed class PinChatMessageRequest
{
    public bool Pin { get; set; } = true;
}

public sealed class CreateDirectConversationRequest
{
    public Guid UserId { get; set; }
}

public sealed class PinChatSessionRequest
{
    public bool Pin { get; set; } = true;
}

public sealed class MarkChatSessionReadRequest
{
    public Guid? MessageId { get; set; }
}
