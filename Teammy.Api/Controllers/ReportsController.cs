using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Reports;
using Teammy.Application.Reports.Dtos;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IReportExportService exportService) : ControllerBase
{
    private readonly IReportExportService _exportService = exportService;

    [HttpPost("export")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<IActionResult> Export([FromBody] ReportRequest request, CancellationToken ct)
    {
        var result = await _exportService.ExportAsync(request, ct);
        return File(
            result.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.FileName);
    }
}
