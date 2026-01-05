using System.IO;
using Teammy.Application.Kanban.Dtos;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Application.Files;

namespace Teammy.Application.Kanban.Services;

public sealed class KanbanService(
    IKanbanReadOnlyQueries read,
    IKanbanRepository repo,
    IGroupAccessQueries access,
    IFileStorage storage
)
{
    // Board
    public async Task<BoardVm> GetBoardAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        if (!await HasMemberOrMentorAccess(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member or mentor");
        await repo.EnsureBoardForGroupAsync(groupId, ct); 
        var vm = await read.GetBoardAsync(groupId, ct);
        return vm ?? throw new KeyNotFoundException("Board not found");
    }

    // Columns
    public async Task<Guid> CreateColumnAsync(Guid groupId, Guid currentUserId, CreateColumnRequest req, CancellationToken ct)
    {
        if (!await access.IsLeaderAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Only leader can manage columns");

        var boardId = await repo.EnsureBoardForGroupAsync(groupId, ct);
        return await repo.CreateColumnAsync(boardId, req.ColumnName.Trim(), req.Position, ct);
    }

    public async Task UpdateColumnAsync(Guid groupId, Guid columnId, Guid currentUserId, UpdateColumnRequest req, CancellationToken ct)
    {
        if (!await access.IsLeaderAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Only leader can manage columns");
        await repo.UpdateColumnAsync(columnId, req.ColumnName.Trim(), req.Position, req.IsDone, req.DueDate, ct);
    }

    public async Task DeleteColumnAsync(Guid groupId, Guid columnId, Guid currentUserId, CancellationToken ct)
    {
        if (!await access.IsLeaderAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Only leader can manage columns");
        await repo.DeleteColumnAsync(columnId, ct);
    }

    // Tasks
    public async Task<Guid> CreateTaskAsync(Guid groupId, Guid currentUserId, CreateTaskRequest req, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        await repo.EnsureBoardForGroupAsync(groupId, ct);
        var taskId = await repo.CreateTaskAsync(groupId, req.ColumnId, req.Title.Trim(), req.Description, req.Priority, req.Status, req.DueDate, req.BacklogItemId, ct);
        
        // Set assignees nếu có
        if (req.AssigneeIds is not null && req.AssigneeIds.Count > 0)
        {
            await repo.ReplaceAssigneesAsync(taskId, req.AssigneeIds, ct);
        }
        
        return taskId;
    }

    public async Task UpdateTaskAsync(Guid groupId, Guid taskId, Guid currentUserId, UpdateTaskRequest req, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        await repo.UpdateTaskAsync(taskId, req.ColumnId, req.Title.Trim(), req.Description, req.Priority, req.Status, req.DueDate, req.BacklogItemId, ct);
        
        // Update assignees nếu có
        if (req.AssigneeIds is not null)
        {
            await repo.ReplaceAssigneesAsync(taskId, req.AssigneeIds, ct);
        }
    }

    public async Task DeleteTaskAsync(Guid groupId, Guid taskId, Guid currentUserId, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        await repo.DeleteTaskAsync(taskId, ct);
    }

    public async Task<MoveTaskResponse> MoveTaskAsync(Guid groupId, Guid taskId, Guid currentUserId, MoveTaskRequest req, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        return await repo.MoveTaskAsync(groupId, taskId, req, ct);
    }

    public async Task ReplaceAssigneesAsync(Guid groupId, Guid taskId, Guid currentUserId, ReplaceAssigneesRequest req, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        await repo.ReplaceAssigneesAsync(taskId, req.UserIds, ct);
    }

    // Comments
    public async Task<Guid> AddCommentAsync(Guid groupId, Guid taskId, Guid currentUserId, CreateCommentRequest req, CancellationToken ct)
    {
        if (!await HasMemberOrMentorAccess(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member or mentor");
        return await repo.AddCommentAsync(taskId, currentUserId, req.Content.Trim(), ct);
    }

    public async Task<IReadOnlyList<CommentVm>> GetCommentsAsync(Guid groupId, Guid taskId, Guid currentUserId, CancellationToken ct)
    {
        if (!await HasMemberOrMentorAccess(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member or mentor");
        return await read.GetCommentsAsync(taskId, ct);
    }

    public async Task DeleteCommentAsync(Guid groupId, Guid commentId, Guid currentUserId, CancellationToken ct)
    {
        if (!await HasMemberOrMentorAccess(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member or mentor");
        await repo.DeleteCommentAsync(commentId, currentUserId, ct);
    }

    private async Task<bool> HasMemberOrMentorAccess(Guid groupId, Guid userId, CancellationToken ct)
        => await access.IsMemberAsync(groupId, userId, ct)
           || await access.IsMentorAsync(groupId, userId, ct);

    // Shared files
    public async Task<UploadFileResult> UploadFileAsync(Guid groupId, Guid currentUserId, Guid? taskId, Stream stream, string fileName, string? description, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");

        var normalizedName = NormalizeFileName(fileName);
        var (url, type, size) = await storage.SaveAsync(stream, normalizedName, ct);
        var id = await repo.AddSharedFileAsync(groupId, currentUserId, taskId, normalizedName, url, type, size, description, ct);
        return new UploadFileResult(id, normalizedName, url, type, size, taskId);
    }

    public async Task<IReadOnlyList<SharedFileVm>> GetFilesByGroupAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        return await read.GetFilesByGroupAsync(groupId, ct);
    }

    public async Task<IReadOnlyList<SharedFileVm>> GetFilesByTaskAsync(Guid groupId, Guid taskId, Guid currentUserId, CancellationToken ct)
    {
        if (!await HasMemberOrMentorAccess(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member or mentor");
        return await read.GetFilesByTaskAsync(taskId, ct);
    }

    public async Task DeleteFileAsync(Guid groupId, Guid fileId, Guid currentUserId, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, currentUserId, ct))
            throw new UnauthorizedAccessException("Not a group member");
        await repo.DeleteSharedFileAsync(fileId, currentUserId, ct);
    }

    private static string NormalizeFileName(string raw)
    {
        var name = Path.GetFileName(raw) ?? string.Empty;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return $"attachment_{DateTime.UtcNow:yyyyMMddHHmmss}";
        return name.Length > 255 ? name[..255] : name;
    }
}
