using System;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Kanban.Dtos;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class KanbanReadOnlyQueries(AppDbContext db) : IKanbanReadOnlyQueries
{

public async Task<BoardVm?> GetBoardAsync(Guid groupId, string? status, int? page, int? pageSize, CancellationToken ct)
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

    var taskQuery = db.tasks.AsNoTracking()
        .Where(t => t.group_id == groupId && colIds.Contains(t.column_id));

    if (!string.IsNullOrWhiteSpace(status))
    {
        var normalized = status.Trim().ToLowerInvariant();
        taskQuery = taskQuery.Where(t => t.status != null && t.status.ToLower() == normalized);
    }

    taskQuery = taskQuery
        .OrderBy(t => t.column_id)
        .ThenBy(t => t.sort_order)
        .ThenBy(t => t.created_at);

    if (page.HasValue || pageSize.HasValue)
    {
        var normalizedPage = Math.Max(page ?? 1, 1);
        var normalizedSize = Math.Clamp(pageSize ?? 50, 1, 200);
        var offset = (normalizedPage - 1) * normalizedSize;
        taskQuery = taskQuery.Skip(offset).Take(normalizedSize);
    }

    var tasks = await taskQuery
        .Select(t => new
        {
            t.task_id,
            t.column_id,
            t.title,
            t.description,
            t.priority,
            t.status,
            t.due_date,
            t.backlog_item_id,
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
                 t.backlog_item_id,
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
    var list = await (
        from c in db.comments.AsNoTracking()
        join u in db.users.AsNoTracking() on c.user_id equals u.user_id
        where c.task_id == taskId
        orderby c.created_at
        select new CommentVm(
            c.comment_id,
            c.task_id,
            c.user_id,
            c.content,
            c.created_at,
            u.display_name,
            u.email,
            u.avatar_url
        )
    ).ToListAsync(ct);
    return list; // List<CommentVm> implements IReadOnlyList<CommentVm>
}

public async Task<IReadOnlyList<SharedFileVm>> GetFilesByGroupAsync(Guid groupId, CancellationToken ct)
{
    var list = await db.shared_files.AsNoTracking()
        .Where(f => f.group_id == groupId)
        .OrderByDescending(f => f.created_at)
        .Select(f => new SharedFileVm(
            f.file_id,
            f.group_id,
            f.uploaded_by,
            f.uploaded_byNavigation.display_name ?? string.Empty,
            f.uploaded_byNavigation.avatar_url,
            f.task_id,
            f.file_name ?? f.file_url,
            f.file_url,
            f.file_type,
            f.file_size,
            f.description,
            f.created_at))
        .ToListAsync(ct);
    return list;
}

public async Task<IReadOnlyList<SharedFileVm>> GetFilesByTaskAsync(Guid taskId, CancellationToken ct)
{
    var list = await db.shared_files.AsNoTracking()
        .Where(f => f.task_id == taskId)
        .OrderByDescending(f => f.created_at)
        .Select(f => new SharedFileVm(
            f.file_id,
            f.group_id,
            f.uploaded_by,
            f.uploaded_byNavigation.display_name ?? string.Empty,
            f.uploaded_byNavigation.avatar_url,
            f.task_id,
            f.file_name ?? f.file_url,
            f.file_url,
            f.file_type,
            f.file_size,
            f.description,
            f.created_at))
        .ToListAsync(ct);
    return list;
}
}
