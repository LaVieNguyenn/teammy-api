namespace Teammy.Application.Kanban.Dtos;

public sealed record BoardVm(
    Guid BoardId, Guid GroupId, string BoardName, IReadOnlyList<ColumnVm> Columns
);

public sealed record ColumnVm(
    Guid ColumnId, string ColumnName, int Position, bool IsDone, DateTime? DueDate,
    IReadOnlyList<TaskVm> Tasks
);

public sealed record TaskVm(
    Guid TaskId,
    Guid ColumnId,
    string Title,
    string? Description,
    string? Priority,
    string? Status,
    DateTime? DueDate,
    Guid? BacklogItemId,
    decimal SortOrder,
    IReadOnlyList<AssigneeVm> Assignees
);

public sealed record AssigneeVm(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl
);

public sealed record CreateColumnRequest(string ColumnName, int? Position);
public sealed record UpdateColumnRequest(string ColumnName, int Position, bool IsDone, DateTime? DueDate);

public sealed record CreateTaskRequest(Guid ColumnId, string Title, string? Description, string? Priority, string? Status, DateTime? DueDate, Guid? BacklogItemId);
public sealed record UpdateTaskRequest(Guid? ColumnId, string Title, string? Description, string? Priority, string? Status, DateTime? DueDate, Guid? BacklogItemId);

public sealed record ReplaceAssigneesRequest(IReadOnlyList<Guid> UserIds);

public sealed record CreateCommentRequest(string Content);
public sealed record CommentVm(Guid CommentId, Guid TaskId, Guid UserId, string Content, DateTime CreatedAt);

public sealed record MoveTaskRequest(Guid ColumnId, Guid? PrevTaskId, Guid? NextTaskId);
public sealed record MoveTaskResponse(Guid TaskId, Guid ColumnId, decimal SortOrder);

public sealed record UploadFileResult(Guid FileId, string FileName, string FileUrl, string? FileType, long? FileSize, Guid? TaskId);
public sealed record SharedFileVm(
    Guid FileId,
    Guid GroupId,
    Guid UploadedBy,
    string UploadedByName,
    string? UploadedByAvatarUrl,
    Guid? TaskId,
    string FileName,
    string FileUrl,
    string? FileType,
    long? FileSize,
    string? Description,
    DateTime CreatedAt
);
