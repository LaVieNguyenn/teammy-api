using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Common.Utils;
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
    private static readonly string[] Headers = TopicImportColumns.All;

    private static readonly string[] Allowed = { "open", "closed", "archived" };

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Topics");

        // Header
        for (int i = 0; i < Headers.Length; i++)
            ws.Cell(1, i + 1).Value = Headers[i];

        ws.Range("A1:G1").Style.Font.Bold = true;
        ws.Columns(1, 7).Width = 30;

        // Sample row 1
        ws.Cell(2, 1).Value = "FALL25";
        ws.Cell(2, 2).Value = "IoT Sensor Hub";
        ws.Cell(2, 3).Value = "Gateway thu thập dữ liệu cảm biến";
        ws.Cell(2, 4).Value = "https://files.school.edu/topics/fall25.pdf";
        ws.Cell(2, 5).Value = "open";
        ws.Cell(2, 6).Value = "Công nghệ thông tin";
        ws.Cell(2, 7).Value = "mentor1@gmail.com; mentor2@gmail.com";

        // Sample row 2
        ws.Cell(3, 1).Value = "SPRING26";
        ws.Cell(3, 2).Value = "E-Commerce Platform";
        ws.Cell(3, 3).Value = "Hệ thống bán hàng đa kênh";
        ws.Cell(3, 4).Value = "";
        ws.Cell(3, 5).Value = "closed";
        ws.Cell(3, 6).Value = "";
        ws.Cell(3, 7).Value = "mentor@gmail.com";

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
            string sourceRaw    = ws.Cell(r, 4).GetString().Trim();
            string stsRaw       = ws.Cell(r, 5).GetString().Trim();
            string mj           = ws.Cell(r, 6).GetString().Trim();
            string rawMentors   = ws.Cell(r, 7).GetString().Trim();

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

            if (!TryNormalizeSource(sourceRaw, out var normalizedSource, out var sourceError))
            {
                errors.Add($"Row {r}: {sourceError}");
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
                normalizedSource,
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

    public async Task<TopicImportValidationResult> ValidateRowsAsync(
        IReadOnlyList<TopicImportPayloadRow> rows,
        CancellationToken ct)
    {
        var safeRows = rows ?? Array.Empty<TopicImportPayloadRow>();
        var results = new List<TopicImportRowValidation>(safeRows.Count);

        var semesterCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var majorCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var mentorCache = new Dictionary<string, (bool IsValid, string? Error)>(StringComparer.OrdinalIgnoreCase);

        int validCount = 0, invalidCount = 0;

        foreach (var row in safeRows)
        {
            ct.ThrowIfCancellationRequested();

            var columns = new List<TopicColumnValidation>(Headers.Length);
            var messages = new List<string>();

            var rowNumber = row is not null && row.RowNumber > 0
                ? row.RowNumber
                : results.Count + 1;
            string semesterCode = row?.SemesterCode?.Trim() ?? string.Empty;
            string title = row?.Title?.Trim() ?? string.Empty;
            string description = row?.Description?.Trim() ?? string.Empty;
            string source = row?.Source?.Trim() ?? string.Empty;
            string status = row?.Status?.Trim() ?? string.Empty;
            string major = row?.MajorName?.Trim() ?? string.Empty;
            var mentorEmails = row?.MentorEmails ?? Array.Empty<string>();

            bool rowIsEmpty = string.IsNullOrWhiteSpace(semesterCode)
                              && string.IsNullOrWhiteSpace(title)
                              && string.IsNullOrWhiteSpace(description)
                              && string.IsNullOrWhiteSpace(source)
                              && string.IsNullOrWhiteSpace(status)
                              && string.IsNullOrWhiteSpace(major)
                              && (mentorEmails.Count == 0 || mentorEmails.All(string.IsNullOrWhiteSpace));

            if (rowIsEmpty)
            {
                const string emptyMessage = "Row is empty";
                columns.Add(new TopicColumnValidation(TopicImportColumns.SemesterCode, false, emptyMessage));
                columns.Add(new TopicColumnValidation(TopicImportColumns.Title, false, emptyMessage));
                columns.Add(new TopicColumnValidation(TopicImportColumns.Description, false, emptyMessage));
                columns.Add(new TopicColumnValidation(TopicImportColumns.Source, false, emptyMessage));
                columns.Add(new TopicColumnValidation(TopicImportColumns.Status, false, emptyMessage));
                columns.Add(new TopicColumnValidation(TopicImportColumns.MajorName, false, emptyMessage));
                columns.Add(new TopicColumnValidation(TopicImportColumns.MentorEmails, false, emptyMessage));
                messages.Add("Row has no data");
                invalidCount++;
                results.Add(new TopicImportRowValidation(rowNumber, false, columns, messages));
                continue;
            }

            // SemesterCode
            bool semesterValid = true;
            string? semesterError = null;
            if (string.IsNullOrWhiteSpace(semesterCode))
            {
                semesterValid = false;
                semesterError = "SemesterCode is required";
            }
            else if (!semesterCache.TryGetValue(semesterCode, out var cachedSemesterValid))
            {
                try
                {
                    _ = SemesterCode.Parse(semesterCode);
                    semesterCache[semesterCode] = true;
                }
                catch (Exception ex)
                {
                    semesterCache[semesterCode] = false;
                    semesterValid = false;
                    semesterError = ex.Message;
                }
            }
            else
            {
                semesterValid = cachedSemesterValid;
                if (!cachedSemesterValid)
                    semesterError = "SemesterCode format is invalid";
            }
            columns.Add(new TopicColumnValidation(TopicImportColumns.SemesterCode, semesterValid, semesterError));

            // Title
            bool titleValid = !string.IsNullOrWhiteSpace(title);
            columns.Add(new TopicColumnValidation(TopicImportColumns.Title, titleValid, titleValid ? null : "Title is required"));

            columns.Add(new TopicColumnValidation(TopicImportColumns.Description, true, null));

            bool sourceValid = true;
            string? sourceError = null;
            if (!string.IsNullOrWhiteSpace(source))
            {
                if (!TryNormalizeSource(source, out _, out sourceError))
                {
                    sourceValid = false;
                }
            }
            columns.Add(new TopicColumnValidation(TopicImportColumns.Source, sourceValid, sourceError));

            // Status
            bool statusValid = true;
            string? statusError = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                status = status.ToLowerInvariant();
                if (!Allowed.Contains(status))
                {
                    statusValid = false;
                    statusError = "Status must be open, closed, or archived";
                }
            }
            columns.Add(new TopicColumnValidation(TopicImportColumns.Status, statusValid, statusError));

            // Major
            bool majorValid = true;
            string? majorError = null;
            if (!string.IsNullOrWhiteSpace(major))
            {
                if (!majorCache.TryGetValue(major, out var exists))
                {
                    var majorId = await read.FindMajorIdByNameAsync(major, ct);
                    exists = majorId is not null;
                    majorCache[major] = exists;
                }

                if (!exists)
                {
                    majorValid = false;
                    majorError = $"Major '{major}' not found";
                }
            }
            columns.Add(new TopicColumnValidation(TopicImportColumns.MajorName, majorValid, majorError));

            // Mentors
            bool mentorsValid = true;
            var mentorErrors = new List<string>();
            var uniqueMentors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in mentorEmails ?? Array.Empty<string>())
            {
                var email = raw?.Trim();
                if (string.IsNullOrWhiteSpace(email)) continue;

                if (!uniqueMentors.Add(email))
                {
                    mentorsValid = false;
                    mentorErrors.Add($"Duplicate mentor email '{email}' in row");
                    continue;
                }

                if (!IsValidEmail(email))
                {
                    mentorsValid = false;
                    mentorErrors.Add($"Mentor email '{email}' format is invalid");
                    continue;
                }

                var mentorCheck = await EnsureMentorAsync(email, mentorCache, ct);
                if (!mentorCheck.IsValid)
                {
                    mentorsValid = false;
                    mentorErrors.Add(mentorCheck.Error ?? $"Mentor '{email}' not found");
                }
            }

            columns.Add(new TopicColumnValidation(
                TopicImportColumns.MentorEmails,
                mentorsValid,
                mentorsValid ? null : string.Join("; ", mentorErrors)));

            bool rowValid = columns.All(c => c.IsValid);
            if (rowValid) validCount++; else invalidCount++;

            results.Add(new TopicImportRowValidation(rowNumber, rowValid, columns, messages));
        }

        var summary = new TopicValidationSummary(safeRows.Count, validCount, invalidCount);
        return new TopicImportValidationResult(summary, results);

        async Task<(bool IsValid, string? Error)> EnsureMentorAsync(
            string email,
            IDictionary<string, (bool IsValid, string? Error)> cache,
            CancellationToken token)
        {
            if (cache.TryGetValue(email, out var cached))
                return cached;

            try
            {
                await mentorLookup.GetMentorIdByEmailAsync(email, token);
                cache[email] = (true, null);
                return (true, null);
            }
            catch (Exception ex)
            {
                cache[email] = (false, ex.Message);
                return (false, ex.Message);
            }
        }

        static bool IsValidEmail(string email)
        {
            try { _ = new MailAddress(email); return true; }
            catch { return false; }
        }
    }

    // ===== Helper parse danh sách email mentor =====
    private static bool TryNormalizeSource(string? raw, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Source must be a valid http(s) link";
            return false;
        }

        normalized = uri.ToString();
        return true;
    }

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
