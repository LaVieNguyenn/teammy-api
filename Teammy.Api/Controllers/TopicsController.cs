using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Topics.Dtos;
using Teammy.Application.Topics.Services;

namespace Teammy.Api.Controllers
{
    [ApiController]
    [Route("api/topics")]
    public sealed class TopicsController : ControllerBase
    {
        private readonly TopicsService _service;

        public TopicsController(TopicsService service)
        {
            _service = service;
        }

        private Guid GetUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                   ?? User.FindFirstValue("sub");

            if (!Guid.TryParse(sub, out var id))
                throw new UnauthorizedAccessException("Invalid token");

            return id;
        }

        // GET /api/topics
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<IReadOnlyList<TopicListItemDto>>> GetAll(
            [FromQuery] string? q,
            [FromQuery] Guid? semesterId,
            [FromQuery] string? status,
            [FromQuery] Guid? majorId,
            [FromQuery] string? ownedBy,
            CancellationToken ct)
        {
            Guid? ownerId = null;
            if (!string.IsNullOrWhiteSpace(ownedBy))
            {
                var normalized = ownedBy.Trim().ToLowerInvariant();
                if (normalized == "me")
                {
                    if (!User.Identity?.IsAuthenticated ?? true)
                        return Unauthorized();
                    ownerId = GetUserId();
                }
                else if (normalized != "all")
                {
                    return BadRequest("ownedBy must be 'me', 'all', or omitted.");
                }
            }

            var list = await _service.GetAllAsync(q, semesterId, status, majorId, ownerId, ct);
            return Ok(list);
        }

        // GET /api/topics/{id}
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<ActionResult<TopicDetailDto>> GetById(Guid id, CancellationToken ct)
        {
            var dto = await _service.GetByIdAsync(id, ct);
            if (dto is null) return NotFound();
            return Ok(dto);
        }

        // POST /api/topics
        [HttpPost]
        [Authorize(Roles = "moderator")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromForm] CreateTopicFormRequest form, CancellationToken ct)
        {
            try
            {
                if (form is null)
                    return BadRequest("Body is required.");

                var req = new CreateTopicRequest(
                    form.SemesterId,
                    form.MajorId,
                    form.Title,
                    form.Description,
                    form.Status,
                    form.MentorEmails ?? new System.Collections.Generic.List<string>());

                var id = await _service.CreateAsync(GetUserId(), req, ct);

                if (form.RegistrationFile is not null && form.RegistrationFile.Length > 0)
                {
                    var ext = Path.GetExtension(form.RegistrationFile.FileName);
                    if (!string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Registration file must be .docx or .txt.");

                    await using var stream = form.RegistrationFile.OpenReadStream();
                    await _service.ReplaceRegistrationFileAsync(id, stream, form.RegistrationFile.FileName, ct);
                }

                return CreatedAtAction(nameof(GetById), new { id }, new { id });
            }
            catch (ArgumentException ex)         { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        }

        // PUT /api/topics/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "moderator")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Update(Guid id, [FromForm] UpdateTopicFormRequest form, CancellationToken ct)
        {
            try
            {
                if (form is null)
                    return BadRequest("Body is required.");

                var req = new UpdateTopicRequest(
                    form.MajorId,
                    form.Title,
                    form.Description,
                    form.Status,
                    form.MentorEmails ?? new System.Collections.Generic.List<string>());

                await _service.UpdateAsync(id, req, ct);

                if (form.RegistrationFile is not null && form.RegistrationFile.Length > 0)
                {
                    var ext = Path.GetExtension(form.RegistrationFile.FileName);
                    if (!string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Registration file must be .docx or .txt.");

                    await using var stream = form.RegistrationFile.OpenReadStream();
                    await _service.ReplaceRegistrationFileAsync(id, stream, form.RegistrationFile.FileName, ct);
                }

                return NoContent();
            }
            catch (KeyNotFoundException)         { return NotFound(); }
            catch (ArgumentException ex)         { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        }

        public sealed class CreateTopicFormRequest
        {
            public Guid SemesterId { get; init; }
            public Guid? MajorId { get; init; }
            public string Title { get; init; } = string.Empty;
            public string? Description { get; init; }
            public string Status { get; init; } = "open";
            public System.Collections.Generic.List<string>? MentorEmails { get; init; }
            public IFormFile? RegistrationFile { get; init; }
        }

        public sealed class UpdateTopicFormRequest
        {
            public Guid? MajorId { get; init; }
            public string Title { get; init; } = string.Empty;
            public string? Description { get; init; }
            public string Status { get; init; } = "open";
            public System.Collections.Generic.List<string>? MentorEmails { get; init; }
            public IFormFile? RegistrationFile { get; init; }
        }

        // DELETE /api/topics/{id}
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "moderator")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }

        // GET /api/topics/template
        [HttpGet("template")]
        //[Authorize(Roles = "moderator")]
        public async Task<IActionResult> GetTemplate(CancellationToken ct)
        {
            var bytes = await _service.BuildTemplateAsync(ct);
            return File(bytes,
                "application/zip",
                "TopicRegistrationPackage.zip");
        }

        // POST /api/topics/import
        [HttpPost("import")]
        //[Authorize(Roles = "moderator")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024, ValueLengthLimit = int.MaxValue)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return BadRequest("File is required.");

            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Please upload a .zip package that contains Topics.xlsx and the registration files.");

            await using var s = file.OpenReadStream();
            var result = await _service.ImportAsync(GetUserId(), s, ct);
            return Ok(result);
        }

        [HttpPost("import/validate")]
        //[Authorize(Roles = "moderator")]
        public async Task<IActionResult> ValidateImport(
            [FromBody] TopicImportValidationRequest request,
            CancellationToken ct)
        {
            if (request is null || request.Rows is null)
            {
                var row = new TopicImportRowValidation(
                    RowNumber: 0,
                    IsValid: false,
                    Columns: new[] { new TopicColumnValidation("Rows", false, "Rows payload is required.") },
                    Messages: new[] { "Rows payload is required." });
                var summary = new TopicValidationSummary(0, 0, 1);
                return Ok(new TopicImportValidationResult(summary, new[] { row }));
            }

            var result = await _service.ValidateImportAsync(request, ct);
            return Ok(result);
        }
    }
}
