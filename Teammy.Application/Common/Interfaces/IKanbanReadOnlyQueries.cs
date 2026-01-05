using Teammy.Application.Kanban.Dtos;

namespace Teammy.Application.Kanban.Interfaces;

public interface IKanbanReadOnlyQueries
{
    Task<BoardVm?> GetBoardAsync(Guid groupId, string? status, int? page, int? pageSize, CancellationToken ct);
    Task<IReadOnlyList<CommentVm>> GetCommentsAsync(Guid taskId, CancellationToken ct);
    Task<IReadOnlyList<SharedFileVm>> GetFilesByGroupAsync(Guid groupId, CancellationToken ct);
    Task<IReadOnlyList<SharedFileVm>> GetFilesByTaskAsync(Guid taskId, CancellationToken ct);
}
