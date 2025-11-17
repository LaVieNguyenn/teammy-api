using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;

namespace Teammy.Infrastructure.Excel;

public sealed class ExcelTopicImportService(
    ITopicWriteRepository repo,
    ITopicReadOnlyQueries read,
    ISemesterWriteRepository semesters,
    IMentorLookupService mentorLookup,
    ITopicMentorService topicMentors
) : ITopicImportService
{
    private static readonly string[] Headers =
    {
        "SemesterCode",  // A
        "Title",         // B
        "Description",   // C
        "Status",        // D
        "MajorName",     // E
        "MentorEmails"   // F  (nhiều email, ngăn bởi ; hoặc ,)
    };

    private static readonly string[] Allowed = { "open", "closed", "archived" };

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Topics");

        // Header
        for (int i = 0; i < Headers.Length; i++)
            ws.Cell(1, i + 1).Value = Headers[i];

        ws.Range("A1:F1").Style.Font.Bold = true;
        ws.Columns(1, 6).Width = 30;

        // Sample row 1
        ws.Cell(2, 1).Value = "FALL25";
        ws.Cell(2, 2).Value = "IoT Sensor Hub";
        ws.Cell(2, 3).Value = "Gateway thu thập dữ liệu cảm biến";
        ws.Cell(2, 4).Value = "open";
        ws.Cell(2, 5).Value = "Công nghệ thông tin";
        ws.Cell(2, 6).Value = "mentor1@gmail.com; mentor2@gmail.com";

        // Sample row 2
        ws.Cell(3, 1).Value = "SPRING26";
        ws.Cell(3, 2).Value = "E-Commerce Platform";
        ws.Cell(3, 3).Value = "Hệ thống bán hàng đa kênh";
        ws.Cell(3, 4).Value = "closed";
        ws.Cell(3, 5).Value = "";
        ws.Cell(3, 6).Value = "mentor@gmail.com";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        await Task.CompletedTask; // để method async-friendly
        return ms.ToArray();
    }

    public async Task<TopicImportResult> ImportAsync(
        Stream excelStream,
        Guid performedBy,
        CancellationToken ct)
    {
        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheet("Topics");
        int last = ws.LastRowUsed()?.RowNumber() ?? 1;

        int total = 0, created = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        for (int r = 2; r <= last; r++)
        {
            ct.ThrowIfCancellationRequested();

            string rawSem       = ws.Cell(r, 1).GetString().Trim();
            string ttl          = ws.Cell(r, 2).GetString().Trim();
            string des          = ws.Cell(r, 3).GetString().Trim();
            string stsRaw       = ws.Cell(r, 4).GetString().Trim();
            string mj           = ws.Cell(r, 5).GetString().Trim();
            string rawMentors   = ws.Cell(r, 6).GetString().Trim();

            // Nếu cả SemesterCode lẫn Title trống thì coi như dòng trống
            if (string.IsNullOrWhiteSpace(rawSem) && string.IsNullOrWhiteSpace(ttl))
                continue;

            total++;

            if (string.IsNullOrWhiteSpace(rawSem))
            {
                errors.Add($"Row {r}: SemesterCode required");
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(ttl))
            {
                errors.Add($"Row {r}: Title required");
                skipped++;
                continue;
            }

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

            // Major
            Guid? majorId = null;
            if (!string.IsNullOrWhiteSpace(mj))
            {
                majorId = await read.FindMajorIdByNameAsync(mj, ct);
                if (majorId is null)
                {
                    errors.Add($"Row {r}: MajorName '{mj}' not found");
                    skipped++;
                    continue;
                }
            }

            // Status
            var sts = string.IsNullOrWhiteSpace(stsRaw)
                ? "open"
                : stsRaw.Trim().ToLowerInvariant();

            if (!Allowed.Contains(sts))
            {
                errors.Add($"Row {r}: Status must be open|closed|archived");
                skipped++;
                continue;
            }

            // Upsert Topic (semester + title)
            var (topicId, isCreated) = await repo.UpsertAsync(
                semesterId,
                ttl,
                string.IsNullOrWhiteSpace(des) ? null : des,
                sts,
                majorId,
                performedBy,
                ct);

            if (isCreated) created++; else updated++;

            var emails = ParseEmails(rawMentors);

            var mentorIds = new List<Guid>();

            foreach (var email in emails)
            {
                try
                {
                    var id = await mentorLookup.GetMentorIdByEmailAsync(email, ct);
                    mentorIds.Add(id);
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {r}: Mentor '{email}' error: {ex.Message}");
                }
            }

            await topicMentors.ReplaceMentorsAsync(topicId, mentorIds, ct);
        }

        return new TopicImportResult(total, created, updated, skipped, errors);
    }

    // ===== Helper parse danh sách email mentor =====
    private static List<string> ParseEmails(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        return raw
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
