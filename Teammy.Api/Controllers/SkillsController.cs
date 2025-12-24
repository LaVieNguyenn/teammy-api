using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Skills.Dtos;
using Teammy.Application.Skills.Services;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/skills")]
public sealed class SkillsController : ControllerBase
{
    private readonly SkillDictionaryService _service;
    private readonly ISkillDictionaryWriteRepository _write;

    public SkillsController(SkillDictionaryService service, ISkillDictionaryWriteRepository write)
    {
        _service = service;
        _write = write;
    }

    [HttpGet]
    // [Authorize]
    public async Task<ActionResult<IReadOnlyList<SkillDictionaryDto>>> List(
        [FromQuery] string? role,
        [FromQuery] string? major,
        CancellationToken ct)
    {
        var items = await _service.ListAsync(role, major, ct);
        return Ok(items);
    }

    [HttpGet("{token}")]
    // [Authorize]
    public async Task<ActionResult<SkillDictionaryDto>> Get(string token, CancellationToken ct)
    {
        var dto = await _service.GetByTokenAsync(token, ct);
        return Ok(dto);
    }

    [HttpGet("template")]
    [Authorize(Roles = "admin")]
    public IActionResult DownloadTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Skills");
        ws.Cell(1, 1).Value = "Token";
        ws.Cell(1, 2).Value = "Role";
        ws.Cell(1, 3).Value = "Major";
        ws.Cell(1, 4).Value = "Aliases";
        ws.Cell(2, 1).Value = "aspnet-core";
        ws.Cell(2, 2).Value = "Backend";
        ws.Cell(2, 3).Value = "Software Engineering";
        ws.Cell(2, 4).Value = "ASP.NET Core; .NET";

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "SkillsImportTemplate.xlsx");
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<SkillDictionaryDto>> Create(
        [FromBody] CreateSkillDictionaryRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { token = dto.Token }, dto);
    }

    [HttpPut("{token}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<SkillDictionaryDto>> Update(
        string token,
        [FromBody] UpdateSkillDictionaryRequest request,
        CancellationToken ct)
    {
        var dto = await _service.UpdateAsync(token, request, ct);
        return Ok(dto);
    }

    [HttpDelete("{token}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(string token, CancellationToken ct)
    {
        await _service.DeleteAsync(token, ct);
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
        if (!header.TryGetValue("token", out var tokenCol)
            || !header.TryGetValue("role", out var roleCol)
            || !header.TryGetValue("major", out var majorCol))
            return BadRequest("Columns required: Token, Role, Major, Aliases");

        header.TryGetValue("aliases", out var aliasCol);

        var errors = new List<ImportErrorDto>();
        var skippedReasons = new List<ImportErrorDto>();
        var total = 0;
        var created = 0;
        var updated = 0;
        var skipped = 0;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var token = ws.Cell(r, tokenCol).GetString().Trim();
            var role = ws.Cell(r, roleCol).GetString().Trim();
            var major = ws.Cell(r, majorCol).GetString().Trim();
            var rawAliases = aliasCol > 0 ? ws.Cell(r, aliasCol).GetString() : string.Empty;

            if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(role) && string.IsNullOrWhiteSpace(major))
                continue;

            total++;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(major))
            {
                errors.Add(new ImportErrorDto(r, "Token, Role, and Major are required"));
                continue;
            }

            var aliases = ParseAliases(rawAliases);
            try
            {
                if (await _write.TokenExistsAsync(token, ct))
                {
                    await _write.UpdateAsync(token, role, major, aliases, ct);
                    updated++;
                }
                else
                {
                    await _write.CreateAsync(token, role, major, aliases, ct);
                    created++;
                }
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

    private static IReadOnlyList<string> ParseAliases(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
