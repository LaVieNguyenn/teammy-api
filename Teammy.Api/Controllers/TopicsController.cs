using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IdentityModel.Tokens.Jwt;
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
        public Task<IReadOnlyList<TopicListItemDto>> GetAll(
            [FromQuery] string? q,
            [FromQuery] Guid? semesterId,
            [FromQuery] string? status,
            [FromQuery] Guid? majorId,
            CancellationToken ct)
            => _service.GetAllAsync(q, semesterId, status, majorId, ct);

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
        [Authorize(Roles = "admin,mentor")]
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
        [Authorize(Roles = "admin,mentor")]
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
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }

        // GET /api/topics/template
        [HttpGet("template")]
        [Authorize(Roles = "admin,mentor")]
        public async Task<IActionResult> GetTemplate(CancellationToken ct)
        {
            var bytes = await _service.BuildTemplateAsync(ct);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "TeammyTopicsTemplate.xlsx");
        }

        // POST /api/topics/import
        [HttpPost("import")]
        [Authorize(Roles = "admin,mentor")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return BadRequest("File is required.");

            await using var s = file.OpenReadStream();
            var result = await _service.ImportAsync(GetUserId(), s, ct);
            return Ok(result);
        }
    }
}
