using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Teammy.Application.Kanban.Dtos;
using Teammy.Application.Kanban.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/board")]
public sealed class BoardController(KanbanService service) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id)) throw new UnauthorizedAccessException("Invalid token");
        return id;
    }

    // Board
    [HttpGet]
    [Authorize]
    public Task<BoardVm> GetBoard(Guid groupId, CancellationToken ct)
        => service.GetBoardAsync(groupId, GetUserId(), ct);

    // Columns
    [HttpPost("columns")]
    [Authorize]
    public async Task<ActionResult<Guid>> CreateColumn(Guid groupId, [FromBody] CreateColumnRequest req, CancellationToken ct)
        => Ok(await service.CreateColumnAsync(groupId, GetUserId(), req, ct));

    [HttpPut("columns/{columnId:guid}")]
    [Authorize]
    public Task UpdateColumn(Guid groupId, Guid columnId, [FromBody] UpdateColumnRequest req, CancellationToken ct)
        => service.UpdateColumnAsync(groupId, columnId, GetUserId(), req, ct);

    [HttpDelete("columns/{columnId:guid}")]
    [Authorize]
    public Task DeleteColumn(Guid groupId, Guid columnId, CancellationToken ct)
        => service.DeleteColumnAsync(groupId, columnId, GetUserId(), ct);

    // Tasks
    [HttpPost("tasks")]
    [Authorize]
    public async Task<ActionResult<Guid>> CreateTask(Guid groupId, [FromBody] CreateTaskRequest req, CancellationToken ct)
        => Ok(await service.CreateTaskAsync(groupId, GetUserId(), req, ct));

    [HttpPut("tasks/{taskId:guid}")]
    [Authorize]
    public Task UpdateTask(Guid groupId, Guid taskId, [FromBody] UpdateTaskRequest req, CancellationToken ct)
        => service.UpdateTaskAsync(groupId, taskId, GetUserId(), req, ct);

    [HttpDelete("tasks/{taskId:guid}")]
    [Authorize]
    public Task DeleteTask(Guid groupId, Guid taskId, CancellationToken ct)
        => service.DeleteTaskAsync(groupId, taskId, GetUserId(), ct);

    [HttpPost("tasks/{taskId:guid}/move")]
    [Authorize]
    public Task<MoveTaskResponse> MoveTask(Guid groupId, Guid taskId, [FromBody] MoveTaskRequest req, CancellationToken ct)
        => service.MoveTaskAsync(groupId, taskId, GetUserId(), req, ct);

    // Assignees
    [HttpPut("tasks/{taskId:guid}/assignees")]
    [Authorize]
    public Task ReplaceAssignees(Guid groupId, Guid taskId, [FromBody] ReplaceAssigneesRequest req, CancellationToken ct)
        => service.ReplaceAssigneesAsync(groupId, taskId, GetUserId(), req, ct);

    // Comments
    [HttpGet("tasks/{taskId:guid}/comments")]
    [Authorize]
    public Task<IReadOnlyList<CommentVm>> GetComments(Guid groupId, Guid taskId, CancellationToken ct)
        => service.GetCommentsAsync(groupId, taskId, GetUserId(), ct);

    [HttpPost("tasks/{taskId:guid}/comments")]
    [Authorize]
    public async Task<ActionResult<Guid>> AddComment(Guid groupId, Guid taskId, [FromBody] CreateCommentRequest req, CancellationToken ct)
        => Ok(await service.AddCommentAsync(groupId, taskId, GetUserId(), req, ct));

    [HttpDelete("comments/{commentId:guid}")]
    [Authorize]
    public Task DeleteComment(Guid groupId, Guid commentId, CancellationToken ct)
        => service.DeleteCommentAsync(groupId, commentId, GetUserId(), ct);

    // Shared files
    [HttpGet("files")]
    [Authorize]
    public Task<IReadOnlyList<SharedFileVm>> GetGroupFiles(Guid groupId, CancellationToken ct)
        => service.GetFilesByGroupAsync(groupId, GetUserId(), ct);

    [HttpGet("tasks/{taskId:guid}/files")]
    [Authorize]
    public Task<IReadOnlyList<SharedFileVm>> GetTaskFiles(Guid groupId, Guid taskId, CancellationToken ct)
        => service.GetFilesByTaskAsync(groupId, taskId, GetUserId(), ct);

    [HttpPost("files/upload")]
    [Authorize]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadFileResult>> UploadFile(Guid groupId, IFormFile file, [FromForm] Guid? taskId, [FromForm] string? description, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is required.");
        await using var s = file.OpenReadStream();
        var res = await service.UploadFileAsync(groupId, GetUserId(), taskId, s, file.FileName, description, ct);
        return Ok(res);
    }

    [HttpDelete("files/{fileId:guid}")]
    [Authorize]
    public Task DeleteFile(Guid groupId, Guid fileId, CancellationToken ct)
        => service.DeleteFileAsync(groupId, fileId, GetUserId(), ct);
}
