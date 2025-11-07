namespace Teammy.Application.Posts.Dtos;

public sealed class CreateProfilePostRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Skills { get; set; }
    public Guid? MajorId { get; set; }
}

public sealed record ProfilePostSummaryDto(
    Guid   Id,
    Guid   SemesterId,
    string Title,
    string Status,
    Guid?  UserId,
    Guid?  MajorId,
    string? Description,
    string? Skills,
    DateTime CreatedAt
);

public sealed record ProfilePostDetailDto(
    Guid   Id,
    Guid   SemesterId,
    string Title,
    string Status,
    Guid?  UserId,
    Guid?  MajorId,
    string? Description,
    DateTime CreatedAt,
    string? Skills
);
