namespace Teammy.Application.Chat.Dtos;

public sealed record ChatMessageDto(
    Guid MessageId,
    Guid SenderId,
    string SenderDisplayName,
    string SenderEmail,
    string? SenderAvatarUrl,
    string? Type,
    string Content,
    DateTime CreatedAt);

public sealed record ConversationSummaryDto(
    Guid SessionId,
    string Type,
    Guid? GroupId,
    string? GroupName,
    Guid? OtherUserId,
    string? OtherDisplayName,
    string? OtherAvatarUrl,
    string? LastMessage,
    DateTime UpdatedAt);

public sealed class SendChatMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Type { get; set; }
}

public sealed class CreateDirectConversationRequest
{
    public Guid UserId { get; set; }
}