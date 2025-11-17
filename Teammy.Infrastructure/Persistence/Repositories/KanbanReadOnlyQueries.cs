using Microsoft.EntityFrameworkCore;
using Teammy.Application.Kanban.Dtos;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class KanbanReadOnlyQueries(AppDbContext db) : IKanbanReadOnlyQueries
{

public async Task<BoardVm?> GetBoardAsync(Guid groupId, CancellationToken ct)
{
    var board = await db.boards.AsNoTracking()
        .FirstOrDefaultAsync(b => b.group_id == groupId, ct);

    if (board is null) return null;

    var cols = await db.columns.AsNoTracking()
        .Where(c => c.board_id == board.board_id)
        .OrderBy(c => c.position)
        .Select(c => new { c.column_id, c.column_name, c.position, c.is_done, c.due_date })
        .ToListAsync(ct);

    var colIds = cols.Select(c => c.column_id).ToArray();

    var tasks = await db.tasks.AsNoTracking()
        .Where(t => t.group_id == groupId && colIds.Contains(t.column_id))
        .OrderBy(t => t.column_id)
        .ThenBy(t => t.sort_order)
        .ThenBy(t => t.created_at)
        .Select(t => new
        {
            t.task_id,
            t.column_id,
            t.title,
            t.description,
            t.priority,
            t.status,
            t.due_date,
            t.sort_order
        })
        .ToListAsync(ct);

    var taskIds = tasks.Select(t => t.task_id).ToArray();

    var assigneeRows = await db.task_assignments.AsNoTracking()
        .Where(a => taskIds.Contains(a.task_id))
        .Select(a => new
        {
            a.task_id,
            a.user_id,
            a.user.display_name,
            a.user.avatar_url
        })
        .ToListAsync(ct);

    var mapAss = assigneeRows
        .GroupBy(x => x.task_id)
        .ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<AssigneeVm>)g
                .Select(x => new AssigneeVm(
                    x.user_id,
                    x.display_name,
                    x.avatar_url
                ))
                .ToList()
        );

    var colVms = cols.Select(c => new ColumnVm(
        c.column_id,
        c.column_name,
        c.position,
        c.is_done,
        c.due_date,
        tasks.Where(t => t.column_id == c.column_id)
             .Select(t => new TaskVm(
                 t.task_id,
                 t.column_id,
                 t.title,
                 t.description,
                 t.priority,
                 t.status,
                 t.due_date,
                 t.sort_order,
                 mapAss.TryGetValue(t.task_id, out var u)
                     ? u
                     : Array.Empty<AssigneeVm>()
             ))
             .ToList()
    )).ToList();

    return new BoardVm(board.board_id, groupId, board.board_name, colVms);
}


public async Task<IReadOnlyList<CommentVm>> GetCommentsAsync(Guid taskId, CancellationToken ct)
{
    var list = await db.comments.AsNoTracking()
        .Where(c => c.task_id == taskId)
        .OrderBy(c => c.created_at)
        .Select(c => new CommentVm(c.comment_id, c.task_id, c.user_id, c.content, c.created_at))
        .ToListAsync(ct);
    return list; // List<CommentVm> implements IReadOnlyList<CommentVm>
}

public async Task<IReadOnlyList<SharedFileVm>> GetFilesByGroupAsync(Guid groupId, CancellationToken ct)
{
    var list = await db.shared_files.AsNoTracking()
        .Where(f => f.group_id == groupId)
        .OrderByDescending(f => f.created_at)
        .Select(f => new SharedFileVm(f.file_id, f.group_id, f.uploaded_by, f.task_id, f.file_url, f.file_type, f.file_size, f.description, f.created_at))
        .ToListAsync(ct);
    return list;
}

public async Task<IReadOnlyList<SharedFileVm>> GetFilesByTaskAsync(Guid taskId, CancellationToken ct)
{
    var list = await db.shared_files.AsNoTracking()
        .Where(f => f.task_id == taskId)
        .OrderByDescending(f => f.created_at)
        .Select(f => new SharedFileVm(f.file_id, f.group_id, f.uploaded_by, f.task_id, f.file_url, f.file_type, f.file_size, f.description, f.created_at))
        .ToListAsync(ct);
    return list;
}
}
