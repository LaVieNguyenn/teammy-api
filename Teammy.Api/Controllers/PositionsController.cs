using ClosedXML.Excel;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/positions")]
public sealed class PositionsController(
    IPositionReadOnlyQueries read,
    IPositionWriteRepository write,
    IMajorReadOnlyQueries majors) : ControllerBase
{
    public sealed record CreatePositionRequest(Guid MajorId, string PositionName);
    public sealed record UpdatePositionRequest(Guid MajorId, string PositionName);

    [HttpGet]
    [Authorize]
    public async Task<ActionResult> List([FromQuery] Guid majorId, CancellationToken ct)
    {
        if (majorId == Guid.Empty) return BadRequest("majorId is required");
        var list = await read.ListByMajorAsync(majorId, ct);
        return Ok(list.Select(x => new { x.PositionId, x.PositionName, MajorId = majorId }));
    }

    [HttpGet("template")]
    [Authorize(Roles = "admin")]
    public IActionResult DownloadTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Positions");
        ws.Cell(1, 1).Value = "Major";
        ws.Cell(1, 2).Value = "Position";
        ws.Cell(2, 1).Value = "Software Engineering";
        ws.Cell(2, 2).Value = "Backend Developer";

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "PositionsImportTemplate.xlsx");
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Create([FromBody] CreatePositionRequest req, CancellationToken ct)
    {
        if (req.MajorId == Guid.Empty) return BadRequest("MajorId is required.");
        if (string.IsNullOrWhiteSpace(req.PositionName)) return BadRequest("PositionName is required.");

        try
        {
            var id = await write.CreateAsync(req.MajorId, req.PositionName, ct);
            return CreatedAtAction(nameof(List), new { majorId = req.MajorId }, new { positionId = id });
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Update([FromRoute] Guid id, [FromBody] UpdatePositionRequest req, CancellationToken ct)
    {
        if (id == Guid.Empty) return BadRequest("PositionId is required.");
        if (req.MajorId == Guid.Empty) return BadRequest("MajorId is required.");
        if (string.IsNullOrWhiteSpace(req.PositionName)) return BadRequest("PositionName is required.");

        try
        {
            await write.UpdateAsync(id, req.MajorId, req.PositionName, ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await write.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import")]
    [Authorize(Roles = "admin")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("file is required");

        await using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws is null) return BadRequest("No worksheet found.");

        var header = ReadHeader(ws);
        if (!header.TryGetValue("major", out var majorCol) || !header.TryGetValue("position", out var posCol))
            return BadRequest("Columns required: Major, Position");

        var errors = new List<ImportErrorDto>();
        var skippedReasons = new List<ImportErrorDto>();
        var total = 0;
        var created = 0;
        var updated = 0;
        var skipped = 0;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var majorName = ws.Cell(r, majorCol).GetString().Trim();
            var positionName = ws.Cell(r, posCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(majorName) && string.IsNullOrWhiteSpace(positionName)) continue;
            total++;

            if (string.IsNullOrWhiteSpace(majorName) || string.IsNullOrWhiteSpace(positionName))
            {
                errors.Add(new ImportErrorDto(r, "Major and Position are required"));
                continue;
            }

            var majorId = await majors.FindMajorIdByNameAsync(majorName, ct);
            if (!majorId.HasValue)
            {
                errors.Add(new ImportErrorDto(r, $"Major not found: {majorName}"));
                continue;
            }

            var existingId = await read.FindPositionIdByNameAsync(majorId.Value, positionName, ct);
            if (existingId.HasValue)
            {
                skipped++;
                skippedReasons.Add(new ImportErrorDto(r, $"Position already exists for major: {majorName}"));
                continue;
            }

            try
            {
                await write.CreateAsync(majorId.Value, positionName, ct);
                created++;
            }
            catch (Exception ex)
            {
                errors.Add(new ImportErrorDto(r, ex.Message));
            }
        }

        return Ok(new ImportResultDto(total, created, updated, skipped, errors, skippedReasons));
    }

    private static Dictionary<string, int> ReadHeader(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = ws.Row(1);
        foreach (var cell in headerRow.CellsUsed())
        {
            var name = cell.GetString().Trim();
            if (name.Length == 0) continue;
            map[name.ToLowerInvariant()] = cell.Address.ColumnNumber;
        }
        return map;
    }
}
