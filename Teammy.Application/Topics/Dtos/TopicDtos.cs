namespace Teammy.Application.Topics.Dtos;

public sealed record TopicListItemDto(
    Guid   TopicId,
    Guid   SemesterId,
    Guid?  MajorId,
    string Title,
    string? Description,
    string Status,       // 'open' | 'closed' | 'archived'
    Guid   CreatedBy,
    DateTime CreatedAt
);

public sealed record TopicDetailDto(
    Guid   TopicId,
    Guid   SemesterId,
    Guid?  MajorId,
    string Title,
    string? Description,
    string Status,
    Guid   CreatedBy,
    DateTime CreatedAt
);

public sealed record CreateTopicRequest(
    Guid   SemesterId,       // NOT NULL
    Guid?  MajorId,          // nullable
    string Title,            // NOT NULL
    string? Description,
    string? Status        
);

public sealed record UpdateTopicRequest(
    Guid?  MajorId,
    string Title,          
    string? Description,
    string Status            // open/closed/archived
);

// Excel import/export
public sealed record TopicImportRow(
    string SemesterCode,     
    string Title,           
    string? Description,
    string? Status,         
    string? MajorName        
);

public sealed record TopicImportResult(
    int TotalRows, int Created, int Updated, int Skipped,
    IReadOnlyList<string> Errors
);
