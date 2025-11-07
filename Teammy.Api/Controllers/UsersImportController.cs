using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/users/import")]
public sealed class UsersImportController : ControllerBase
{
    private readonly IUserImportService _service;
    public UsersImportController(IUserImportService service) => _service = service;

    [HttpGet("template")]
    //[Authorize] 
    public async Task<IActionResult> DownloadTemplate(CancellationToken ct)
    {
        var bytes = await _service.BuildTemplateAsync(ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "TeammyUsersTemplate.xlsx");
    }

    [HttpPost]
    //[Authorize]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is required.");

        var allowed = new[]
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel",
            "application/octet-stream"
        };
        if (!allowed.Contains(file.ContentType))
            return BadRequest("Invalid file type.");
    
        using var stream = file.OpenReadStream();

        // Nếu bạn cần userId người thực hiện: lấy từ JWT claim
        var performedBy = HttpContext.User?.Identity?.IsAuthenticated == true
            ? Guid.TryParse(User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value, out var id) ? id : Guid.Empty
            : Guid.Empty;

        var result = await _service.ImportAsync(stream, performedBy, ct);
        return Ok(result);
    }
}
