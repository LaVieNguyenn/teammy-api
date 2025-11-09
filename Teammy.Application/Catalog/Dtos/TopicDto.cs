namespace Teammy.Application.Catalog.Dtos;

public sealed record TopicDto(
    Guid     TopicId,
    Guid     SemesterId,
    Guid?    MajorId,
    string   Title,
    string?  Description,
    string   Status,
    Guid     CreatedBy,
    DateTime CreatedAt
);

