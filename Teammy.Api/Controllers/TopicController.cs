using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Teammy.Api.Contracts.Topic;
using Teammy.Application.Common.Interfaces.Topics;
namespace Teammy.Api.Controllers;
[ApiController]
[Route("api/topics")]
public sealed class TopicController : ControllerBase
{
    private readonly ITopicImportService _importer;
    private readonly ILogger<TopicController> _log;

    public TopicController(ITopicImportService importer, ILogger<TopicController> log)
    {
        _importer = importer;
        _log = log;
    }
    [HttpPost("import")]
    [Authorize(Roles = "moderator")]
    [Consumes("multipart/form-data", "application/octet-stream", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
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

}
