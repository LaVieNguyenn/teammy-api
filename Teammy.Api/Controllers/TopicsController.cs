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
        public async Task<IActionResult> Create([FromBody] CreateTopicRequest req, CancellationToken ct)
        {
            try
            {
                var id = await _service.CreateAsync(GetUserId(), req, ct);
                return CreatedAtAction(nameof(GetById), new { id }, new { id });
            }
            catch (ArgumentException ex)         { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        }

        // PUT /api/topics/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "moderator")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTopicRequest req, CancellationToken ct)
        {
            try
            {
                await _service.UpdateAsync(id, req, ct);
                return NoContent();
            }
            catch (KeyNotFoundException)         { return NotFound(); }
            catch (ArgumentException ex)         { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
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
                return BadRequest("Rows payload is required.");

            var result = await _service.ValidateImportAsync(request, ct);
            return Ok(result);
        }
    }
}
