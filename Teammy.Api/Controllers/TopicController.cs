using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Api.Contracts.Common;
using Teammy.Api.Contracts.Topic;
using Teammy.Application.Common.Interfaces.Topics;
using Teammy.Application.Topics;
using Teammy.Application.Topics.ReadModels;
namespace Teammy.Api.Controllers;
[ApiController]
[Route("api/topics")]
public sealed class TopicController : ControllerBase
{
    private readonly ITopicImportService _importer;
    private readonly ITopicService _topics;
    private readonly ILogger<TopicController> _log;

    public TopicController(ITopicImportService importer, ITopicService topics, ILogger<TopicController> log)
    {
        _importer = importer;
        _topics = topics;
        _log = log;
    }
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TopicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var vm = await _topics.GetByIdAsync(id, ct);
        if (vm is null) return NotFound();
        return Ok(Map(vm));
    }
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TopicDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
       [FromQuery] Guid termId,
       [FromQuery] string? status,
       [FromQuery] Guid? departmentId,
       [FromQuery] Guid? majorId,
       [FromQuery] string? q,
       [FromQuery] string? sort,
       [FromQuery] int page = 1,
       [FromQuery] int size = 20,
       CancellationToken ct = default)
    {
        if (termId == Guid.Empty)
            return BadRequest(new { error = "TERM_ID_REQUIRED" });

        var res = await _topics.SearchAsync(termId, status, departmentId, majorId, q, sort, page, size, ct);
        return Ok(new PagedResponse<TopicDto>(
            res.Total, res.Page, res.Size,
            res.Items.Select(Map).ToList()
        ));
    }
    [HttpPost("import")]
    [Authorize(Roles = "moderator")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import([FromQuery] Guid termId, [FromQuery] Guid? majorId, CancellationToken ct)
    {
        if (termId == Guid.Empty)
            return BadRequest(new { error = "TERM_ID_REQUIRED" });

        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(uid, out var actorId))
            return Unauthorized();
        try
        {
            Stream? stream = null;
            string fileName = "upload.xlsx";
            bool disposeStream = false;
            if (Request.HasFormContentType)
            {
                try
                {
                    var form = await Request.ReadFormAsync(ct);
                    var f = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                    if (f != null && f.Length > 0)
                    {
                        stream = f.OpenReadStream();
                        disposeStream = true;
                        if (!string.IsNullOrWhiteSpace(f.FileName)) fileName = f.FileName;
                    }
                }
                catch
                {
                }
            }
            if (stream is null && Request.ContentLength.GetValueOrDefault() > 0)
            {
                var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms, ct);
                ms.Position = 0;
                stream = ms;
                disposeStream = true;
            }
            if (stream is null)
                return BadRequest(new { error = "FILE_REQUIRED" });

            var result = await _importer.ImportAsync(termId, stream, fileName, actorId, ct, majorId);

            if (disposeStream)
                await stream.DisposeAsync();

            return Ok(new ImportResponse
            {
                JobId = result.JobId.ToString(),
                Total = result.TotalRows,
                Success = result.SuccessRows,
                Error = result.ErrorRows
            });
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "Import failed");
            return BadRequest(new { error = ex.Message });
        }
    }
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "moderator")]
    [ProducesResponseType(typeof(TopicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateTopicRequest body, CancellationToken ct)
    {
        var r = await _topics.UpdateAsync(id, body.Title, body.Code, body.Description, body.DepartmentId, body.MajorId, ct);
        if (!r.Ok)
            return StatusCode(r.StatusCode, new { error = r.Message });

        var vm = await _topics.GetByIdAsync(id, ct);
        if (vm is null) return NotFound();
        return Ok(Map(vm));
    }
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "moderator")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive([FromRoute] Guid id, CancellationToken ct)
    {
        var r = await _topics.ArchiveAsync(id, ct);
        if (!r.Ok)
            return StatusCode(r.StatusCode, new { error = r.Message });
        return NoContent();
    }

    private static TopicDto Map(TopicReadModel m) => new()
    {
        Id = m.Id,
        TermId = m.TermId,
        Code = m.Code,
        Title = m.Title,
        Description = m.Description,
        DepartmentId = m.DepartmentId,
        MajorId = m.MajorId,
        Status = m.Status,
        CreatedAt = m.CreatedAt
    };

}
