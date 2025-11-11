using ClosedXML.Excel;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;

namespace Teammy.Infrastructure.Excel;

public sealed class ExcelTopicImportService(
    ITopicWriteRepository repo,
    ITopicReadOnlyQueries read,
    ISemesterWriteRepository semesters
) : ITopicImportService
{
    private static readonly string[] Headers = { "SemesterCode","Title","Description","Status","MajorName" };
    private static readonly string[] Allowed = { "open","closed","archived" };

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Topics");
        for (int i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Range("A1:E1").Style.Font.Bold = true;
        ws.Columns(1, 5).Width = 30;

        ws.Cell(2,1).Value = "FALL25";
        ws.Cell(2,2).Value = "IoT Sensor Hub";
        ws.Cell(2,3).Value = "Gateway thu thập dữ liệu cảm biến";
        ws.Cell(2,4).Value = "open";
        ws.Cell(2,5).Value = "Công nghệ thông tin";

        ws.Cell(3,1).Value = "SPRING26";
        ws.Cell(3,2).Value = "E-Commerce Platform";
        ws.Cell(3,3).Value = "Hệ thống bán hàng đa kênh";
        ws.Cell(3,4).Value = "closed";
        ws.Cell(3,5).Value = "";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<TopicImportResult> ImportAsync(Stream excelStream, Guid performedBy, CancellationToken ct)
    {
        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheet("Topics");
        int last = ws.LastRowUsed()?.RowNumber() ?? 1;

        int total=0, created=0, updated=0, skipped=0;
        var errors = new List<string>();

        for (int r = 2; r <= last; r++)
        {
            string rawSem = ws.Cell(r,1).GetString().Trim();
            string ttl    = ws.Cell(r,2).GetString().Trim();
            string des    = ws.Cell(r,3).GetString().Trim();
            string sts    = ws.Cell(r,4).GetString().Trim().ToLowerInvariant();
            string mj     = ws.Cell(r,5).GetString().Trim();

            if (string.IsNullOrWhiteSpace(rawSem) && string.IsNullOrWhiteSpace(ttl)) continue;
            total++;

            if (string.IsNullOrWhiteSpace(rawSem)) { errors.Add($"Row {r}: SemesterCode required"); skipped++; continue; }
            if (string.IsNullOrWhiteSpace(ttl))    { errors.Add($"Row {r}: Title required");        skipped++; continue; }

            Guid semesterId;
            try
            {
                semesterId = await semesters.EnsureByCodeAsync(rawSem, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"Row {r}: Invalid SemesterCode '{rawSem}' ({ex.Message})");
                skipped++; 
                continue;
            }

            Guid? majorId = null;
            if (!string.IsNullOrWhiteSpace(mj))
            {
                majorId = await read.FindMajorIdByNameAsync(mj, ct);
                if (majorId is null) { errors.Add($"Row {r}: MajorName '{mj}' not found"); skipped++; continue; }
            }

            if (string.IsNullOrWhiteSpace(sts)) sts = "open";
            if (!Allowed.Contains(sts)) { errors.Add($"Row {r}: Status must be open|closed|archived"); skipped++; continue; }

            var (id, isCreated) = await repo.UpsertAsync(
                semesterId, ttl,
                string.IsNullOrWhiteSpace(des) ? null : des,
                sts, majorId, performedBy, ct);

            if (isCreated) created++; else updated++;
        }

        return new TopicImportResult(total, created, updated, skipped, errors);
    }
}
