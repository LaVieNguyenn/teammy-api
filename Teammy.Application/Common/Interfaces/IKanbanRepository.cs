using Teammy.Application.Kanban.Dtos;

namespace Teammy.Application.Kanban.Interfaces;

public interface IKanbanRepository
{
    Task<Guid> EnsureBoardForGroupAsync(Guid groupId, CancellationToken ct);

    // Columns
    Task<Guid> CreateColumnAsync(Guid boardId, string name, int? position, CancellationToken ct);
    Task UpdateColumnAsync(Guid columnId, string name, int position, bool isDone, DateTime? dueDate, CancellationToken ct);
    Task DeleteColumnAsync(Guid columnId, CancellationToken ct);

    // Tasks
    Task<Guid> CreateTaskAsync(Guid groupId, Guid columnId, string title, string? description, string? priority, string? status, DateTime? dueDate, CancellationToken ct);
    Task UpdateTaskAsync(Guid taskId, Guid? newColumnId, string title, string? description, string? priority, string? status, DateTime? dueDate, CancellationToken ct);
    Task DeleteTaskAsync(Guid taskId, CancellationToken ct);
    Task<MoveTaskResponse> MoveTaskAsync(Guid groupId, Guid taskId, MoveTaskRequest req, CancellationToken ct);

    // Assignments
    Task ReplaceAssigneesAsync(Guid taskId, IEnumerable<Guid> userIds, CancellationToken ct);

    // Comments
    Task<Guid> AddCommentAsync(Guid taskId, Guid userId, string content, CancellationToken ct);
    Task DeleteCommentAsync(Guid commentId, Guid performedBy, CancellationToken ct);

    // Files
    Task<Guid> AddSharedFileAsync(Guid groupId, Guid uploadedBy, Guid? taskId, string fileUrl, string? fileType, long? fileSize, string? description, CancellationToken ct);
    Task DeleteSharedFileAsync(Guid fileId, Guid performedBy, CancellationToken ct);
}
