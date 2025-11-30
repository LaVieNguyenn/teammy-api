using System;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Kanban.Dtos;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class KanbanRepository(AppDbContext db) : IKanbanRepository
{
    private const decimal GAP = 1000m;
    private const decimal MIN_GAP = 0.000001m;

    public async Task<Guid> EnsureBoardForGroupAsync(Guid groupId, CancellationToken ct)
    {
        var b = await db.boards.FirstOrDefaultAsync(x => x.group_id == groupId, ct);
        if (b is null)
        {
            b = new board { board_id = Guid.NewGuid(), group_id = groupId, board_name = "Board", status = null };
            db.boards.Add(b);
            await db.SaveChangesAsync(ct);
        }

        // Nếu falta column -> seed 3 cột mặc định (To Do / In Progress / Done)
        var hasAnyCol = await db.columns.AsNoTracking().AnyAsync(c => c.board_id == b.board_id, ct);
        if (!hasAnyCol)
        {
            var now = DateTime.UtcNow;
            db.columns.AddRange(
                new column { column_id = Guid.NewGuid(), board_id = b.board_id, column_name = "To Do", position = 1, is_done = false, created_at = now, updated_at = now },
                new column { column_id = Guid.NewGuid(), board_id = b.board_id, column_name = "In Progress", position = 2, is_done = false, created_at = now, updated_at = now },
                new column { column_id = Guid.NewGuid(), board_id = b.board_id, column_name = "Done", position = 3, is_done = true, created_at = now, updated_at = now }
            );
            await db.SaveChangesAsync(ct);
        }

        return b.board_id;
    }

    // Columns
    public async Task<Guid> CreateColumnAsync(Guid boardId, string name, int? position, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var count = await db.columns.Where(c => c.board_id == boardId).CountAsync(ct);
        var desired = Math.Clamp(position ?? (count + 1), 1, count + 1);

        // Dồn đuôi: [desired..N] -> +1
        await db.columns
            .Where(c => c.board_id == boardId && c.position >= desired)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, c => c.position + 1), ct);

        var now = DateTime.UtcNow;
        var e = new column
        {
            column_id = Guid.NewGuid(),
            board_id = boardId,
            column_name = name,
            position = desired,
            is_done = false,
            created_at = now,
            updated_at = now
        };
        db.columns.Add(e);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return e.column_id;
    }

    public async Task UpdateColumnAsync(Guid columnId, string name, int position, bool isDone, DateTime? dueDate, CancellationToken ct)
    {
        var col = await db.columns.FirstOrDefaultAsync(c => c.column_id == columnId, ct)
                 ?? throw new KeyNotFoundException("Column not found");

        var boardId = col.board_id;
        var oldPos = col.position;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var count = await db.columns.Where(c => c.board_id == boardId).CountAsync(ct);
        var newPos = Math.Clamp(position, 1, count);

        if (newPos != oldPos)
        {
            if (newPos < oldPos)
            {
                // Kéo lên: [newPos .. oldPos-1] +1
                await db.columns
                  .Where(c => c.board_id == boardId && c.position >= newPos && c.position < oldPos)
                  .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, c => c.position + 1), ct);
            }
            else
            {
                // Kéo xuống: [oldPos+1 .. newPos] -1
                await db.columns
                  .Where(c => c.board_id == boardId && c.position > oldPos && c.position <= newPos)
                  .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, c => c.position - 1), ct);
            }

            // Đặt cột về vị trí đích
            await db.columns.Where(c => c.column_id == columnId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, newPos), ct);
        }

        // Update meta
        col.column_name = name;
        col.is_done = isDone;
        col.due_date = dueDate;
        col.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task DeleteColumnAsync(Guid columnId, CancellationToken ct)
    {
        var col = await db.columns.FirstOrDefaultAsync(c => c.column_id == columnId, ct)
                 ?? throw new KeyNotFoundException("Column not found");
        var boardId = col.board_id;
        var pos = col.position;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.columns.Remove(col);
        await db.SaveChangesAsync(ct);

        await db.columns
            .Where(c => c.board_id == boardId && c.position > pos)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, c => c.position - 1), ct);

        await tx.CommitAsync(ct);
    }

    // Tasks
    public async Task<Guid> CreateTaskAsync(Guid groupId, Guid columnId, string title, string? description, string? priority, string? status, DateTime? dueDate, Guid? backlogItemId, CancellationToken ct)
    {
        var column = await db.columns.Include(c => c.board)
            .FirstOrDefaultAsync(c => c.column_id == columnId, ct)
            ?? throw new InvalidOperationException("Column not found");
        if (column.board.group_id != groupId)
            throw new InvalidOperationException("Column does not belong to group board");

        backlog_item? backlog = null;
        if (backlogItemId is Guid backlogId)
        {
            backlog = await EnsureBacklogAvailableAsync(backlogId, groupId, null, ct);
        }

        var tail = await db.tasks.Where(t => t.column_id == columnId).MaxAsync(t => (decimal?)t.sort_order, ct) ?? 0m;

        var now = DateTime.UtcNow;
        var e = new task
        {
            task_id = Guid.NewGuid(),
            group_id = groupId,
            column_id = columnId,
            backlog_item_id = backlogItemId,
            title = title,
            description = string.IsNullOrWhiteSpace(description) ? null : description,
            priority = string.IsNullOrWhiteSpace(priority) ? null : priority,
            status = string.IsNullOrWhiteSpace(status) ? null : status,
            due_date = dueDate,
            sort_order = tail + GAP,
            created_at = now,
            updated_at = now
        };
        db.tasks.Add(e);

        if (backlog is not null)
        {
            ApplyBacklogProgress(backlog, column.is_done);
        }

        await db.SaveChangesAsync(ct);
        return e.task_id;
    }

    public async Task UpdateTaskAsync(Guid taskId, Guid? newColumnId, string title, string? description, string? priority, string? status, DateTime? dueDate, Guid? backlogItemId, CancellationToken ct)
    {
        var t = await db.tasks.Include(x => x.column).ThenInclude(c => c.board)
              .FirstOrDefaultAsync(x => x.task_id == taskId, ct)
              ?? throw new KeyNotFoundException("Task not found");

        var targetColumn = t.column;
        var columnChanged = false;

        if (newColumnId is not null && newColumnId.Value != t.column_id)
        {
            var dest = await db.columns.Include(c => c.board)
                .FirstOrDefaultAsync(c => c.column_id == newColumnId, ct)
                ?? throw new InvalidOperationException("Column not found");
            if (dest.board.group_id != t.group_id)
                throw new InvalidOperationException("Column does not belong to group board");

            var tail = await db.tasks.Where(z => z.column_id == newColumnId).MaxAsync(z => (decimal?)z.sort_order, ct) ?? 0m;
            t.column_id = newColumnId.Value;
            t.sort_order = tail + GAP;
            targetColumn = dest;
            columnChanged = true;
        }

        t.title = title;
        t.description = string.IsNullOrWhiteSpace(description) ? null : description;
        t.priority = string.IsNullOrWhiteSpace(priority) ? null : priority;
        t.status = string.IsNullOrWhiteSpace(status) ? null : status;
        t.due_date = dueDate;
        t.updated_at = DateTime.UtcNow;

        var backlogPayloadProvided = backlogItemId.HasValue;
        if (backlogPayloadProvided)
        {
            if (backlogItemId == Guid.Empty)
            {
                if (t.backlog_item_id is Guid existing)
                {
                    await ResetBacklogAsync(existing, targetColumn.is_done, ct);
                    t.backlog_item_id = null;
                }
            }
            else if (backlogItemId != t.backlog_item_id)
            {
                var backlog = await EnsureBacklogAvailableAsync(backlogItemId!.Value, t.group_id, taskId, ct);
                t.backlog_item_id = backlogItemId.Value;
                ApplyBacklogProgress(backlog, targetColumn.is_done);
            }
            else if (columnChanged && t.backlog_item_id is Guid linkedId)
            {
                await UpdateLinkedBacklogAsync(linkedId, targetColumn.is_done, ct);
            }
        }
        else if (columnChanged && t.backlog_item_id is Guid existingBacklogId)
        {
            await UpdateLinkedBacklogAsync(existingBacklogId, targetColumn.is_done, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct)
    {
        var t = await db.tasks.Include(x => x.column)
            .FirstOrDefaultAsync(x => x.task_id == taskId, ct)
            ?? throw new KeyNotFoundException("Task not found");

        var wasDone = t.column?.is_done ?? false;
        await ResetBacklogAsync(t.backlog_item_id, wasDone, ct);

        db.tasks.Remove(t); // CASCADE comments/assignments
        await db.SaveChangesAsync(ct);
    }

    public async Task<MoveTaskResponse> MoveTaskAsync(Guid groupId, Guid taskId, MoveTaskRequest req, CancellationToken ct)
    {
        var task = await db.tasks.FirstOrDefaultAsync(t => t.task_id == taskId, ct)
                 ?? throw new KeyNotFoundException("Task not found");

        var srcCol = await db.columns.Include(c => c.board).FirstAsync(c => c.column_id == task.column_id, ct);
        var dstCol = await db.columns.Include(c => c.board).FirstOrDefaultAsync(c => c.column_id == req.ColumnId, ct)
                    ?? throw new InvalidOperationException("Target column not found");
        if (srcCol.board.group_id != groupId || dstCol.board.group_id != groupId)
            throw new InvalidOperationException("Task/column not in the group's board");

        decimal? prev = null, next = null;
        if (req.PrevTaskId is Guid pId)
            prev = await db.tasks.Where(t => t.task_id == pId && t.column_id == req.ColumnId).Select(t => (decimal?)t.sort_order).FirstOrDefaultAsync(ct);
        if (req.NextTaskId is Guid nId)
            next = await db.tasks.Where(t => t.task_id == nId && t.column_id == req.ColumnId).Select(t => (decimal?)t.sort_order).FirstOrDefaultAsync(ct);

        decimal newSort;
        if (prev is null && next is null) newSort = GAP;
        else if (prev is null) newSort = next!.Value - GAP;
        else if (next is null) newSort = prev!.Value + GAP;
        else
        {
            var gap = next.Value - prev.Value;
            newSort = gap <= MIN_GAP ? await ResequenceAndMidAsync(req.ColumnId, req.PrevTaskId!.Value, req.NextTaskId!.Value, ct)
                                     : prev.Value + gap / 2m;
        }

        task.column_id = req.ColumnId;
        task.sort_order = newSort;
        task.updated_at = DateTime.UtcNow;

        await UpdateLinkedBacklogAsync(task.backlog_item_id, dstCol.is_done, ct);

        await db.SaveChangesAsync(ct);

        return new MoveTaskResponse(taskId, req.ColumnId, newSort);
    }

    private async Task<decimal> ResequenceAndMidAsync(Guid columnId, Guid prevId, Guid nextId, CancellationToken ct)
    {
        var ids = await db.tasks.AsNoTracking()
            .Where(t => t.column_id == columnId)
            .OrderBy(t => t.sort_order).ThenBy(t => t.created_at)
            .Select(t => t.task_id)
            .ToListAsync(ct);

        var i = 1;
        foreach (var id in ids)
        {
            var seq = i * GAP;
            await db.tasks.Where(t => t.task_id == id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.sort_order, _ => seq)
                , ct);
            i++;
        }
        var prev = await db.tasks.Where(t => t.task_id == prevId).Select(t => t.sort_order).FirstAsync(ct);
        var next = await db.tasks.Where(t => t.task_id == nextId).Select(t => t.sort_order).FirstAsync(ct);
        return prev + (next - prev) / 2m;
    }

    // Assignments
    public async Task ReplaceAssigneesAsync(Guid taskId, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var cur = await db.task_assignments.Where(a => a.task_id == taskId).ToListAsync(ct);
        db.task_assignments.RemoveRange(cur);
        foreach (var uid in userIds.Distinct())
        {
            db.task_assignments.Add(new task_assignment
            {
                task_assignment_id = Guid.NewGuid(),
                task_id = taskId,
                user_id = uid,
                assigned_at = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // Comments
    public async Task<Guid> AddCommentAsync(Guid taskId, Guid userId, string content, CancellationToken ct)
    {
        var e = new comment
        {
            comment_id = Guid.NewGuid(),
            task_id = taskId,
            user_id = userId,
            content = content,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        db.comments.Add(e);
        await db.SaveChangesAsync(ct);
        return e.comment_id;
    }

    public async Task DeleteCommentAsync(Guid commentId, Guid performedBy, CancellationToken ct)
    {
        var c = await db.comments.FirstOrDefaultAsync(x => x.comment_id == commentId, ct);
        if (c is null) return;
        db.comments.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    // Files
    public async Task<Guid> AddSharedFileAsync(Guid groupId, Guid uploadedBy, Guid? taskId, string fileName, string fileUrl, string? fileType, long? fileSize, string? description, CancellationToken ct)
    {
        if (taskId.HasValue)
        {
            var ok = await db.tasks.AnyAsync(t => t.task_id == taskId && t.group_id == groupId, ct);
            if (!ok) throw new InvalidOperationException("Task not in this group");
        }

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName.Trim();
        if (safeName.Length > 255)
            safeName = safeName[..255];

        var e = new shared_file
        {
            file_id = Guid.NewGuid(),
            group_id = groupId,
            uploaded_by = uploadedBy,
            task_id = taskId,
            file_name = safeName,
            file_url = fileUrl,
            file_type = fileType,
            file_size = fileSize,
            description = string.IsNullOrWhiteSpace(description) ? null : description,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        db.shared_files.Add(e);
        await db.SaveChangesAsync(ct);
        return e.file_id;
    }

    public async Task DeleteSharedFileAsync(Guid fileId, Guid performedBy, CancellationToken ct)
    {
        var f = await db.shared_files.FirstOrDefaultAsync(x => x.file_id == fileId, ct);
        if (f is null) return;
        db.shared_files.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    private async Task<backlog_item> EnsureBacklogAvailableAsync(Guid backlogItemId, Guid groupId, Guid? ignoreTaskId, CancellationToken ct)
    {
        var backlog = await db.backlog_items.FirstOrDefaultAsync(b => b.backlog_item_id == backlogItemId && b.group_id == groupId, ct)
            ?? throw new InvalidOperationException("Backlog item not found in this group");

        if (string.Equals(backlog.status, "archived", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backlog item is archived");

        var linked = await db.tasks.AsNoTracking()
            .AnyAsync(t => t.backlog_item_id == backlogItemId && (!ignoreTaskId.HasValue || t.task_id != ignoreTaskId.Value), ct);
        if (linked)
            throw new InvalidOperationException("Backlog item already linked to another task");

        return backlog;
    }

    private static void ApplyBacklogProgress(backlog_item backlog, bool columnIsDone)
    {
        backlog.status = columnIsDone ? "completed" : "in_progress";
        backlog.updated_at = DateTime.UtcNow;
    }

    private async Task UpdateLinkedBacklogAsync(Guid? backlogItemId, bool columnIsDone, CancellationToken ct)
    {
        if (backlogItemId is null) return;
        var backlog = await db.backlog_items.FirstOrDefaultAsync(b => b.backlog_item_id == backlogItemId, ct);
        if (backlog is null) return;
        ApplyBacklogProgress(backlog, columnIsDone);
    }

    private async Task ResetBacklogAsync(Guid? backlogItemId, bool columnWasDone, CancellationToken ct)
    {
        if (backlogItemId is null) return;
        var backlog = await db.backlog_items.FirstOrDefaultAsync(b => b.backlog_item_id == backlogItemId, ct);
        if (backlog is null) return;
        backlog.status = columnWasDone ? "completed" : "ready";
        backlog.updated_at = DateTime.UtcNow;
    }

    private async Task ResequenceColumnsAsync(Guid boardId, CancellationToken ct)
    {
        await db.columns
            .Where(c => c.board_id == boardId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, c => c.position + 1000), ct);

        var ids = await db.columns
            .AsNoTracking()
            .Where(c => c.board_id == boardId)
            .OrderBy(c => c.position)
            .ThenBy(c => c.column_id)
            .Select(c => c.column_id)
            .ToListAsync(ct);

        var i = 1;
        foreach (var id in ids)
        {
            var pos = i++;
            await db.columns.Where(c => c.column_id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.position, pos), ct);
        }
    }
}
