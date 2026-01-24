using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Teammy.Application.Ai.SkillExtraction;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Files;
using Teammy.Application.Topics.Dtos;
using Teammy.Application.Common.Utils;

namespace Teammy.Infrastructure.Topics;

public sealed class TopicRegistrationPackageImportService : ITopicImportService
{
    private static readonly string[] Headers = TopicImportColumns.All;
    private static readonly string[] AllowedStatuses = { "open", "closed", "archived" };
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".txt" };
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string LegacyRegistrationHeader = "RegistrationFile";
    private static readonly char[] SkillTokenSeparators =
    {
        '\n', '\r', ',', ';', '-', '–', '—', '/', '\\', '|', '•', '○', '●', '◦', '\t', '&', '+', ':'
    };

    private readonly ITopicWriteRepository _repo;
    private readonly ITopicReadOnlyQueries _read;
    private readonly ISemesterWriteRepository _semesters;
    private readonly IMentorLookupService _mentorLookup;
    private readonly ITopicMentorService _topicMentors;
    private readonly IFileStorage _fileStorage;
    private readonly IMajorReadOnlyQueries _majorQueries;
    private readonly ISkillDictionaryReadOnlyQueries _skillDictionary;
    private readonly IAiLlmClient _llmClient;

    public TopicRegistrationPackageImportService(
        ITopicWriteRepository repo,
        ITopicReadOnlyQueries read,
        ISemesterWriteRepository semesters,
        IMentorLookupService mentorLookup,
        ITopicMentorService topicMentors,
        IFileStorage fileStorage,
        IMajorReadOnlyQueries majorQueries,
        ISkillDictionaryReadOnlyQueries skillDictionary,
        IAiLlmClient llmClient)
    {
        _repo = repo;
        _read = read;
        _semesters = semesters;
        _mentorLookup = mentorLookup;
        _topicMentors = topicMentors;
        _fileStorage = fileStorage;
        _majorQueries = majorQueries;
        _skillDictionary = skillDictionary;
        _llmClient = llmClient;
    }

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct)
    {
        var majors = await _majorQueries.GetAllMajorNamesAsync(ct);
        var sampleMajor1 = majors.FirstOrDefault() ?? "Software Engineering";
        var sampleMajor2 = majors.Skip(1).FirstOrDefault() ?? majors.FirstOrDefault() ?? "Information Systems";

        using var workbookStream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Topics");
            for (int i = 0; i < Headers.Length; i++)
                sheet.Cell(1, i + 1).Value = Headers[i];

            sheet.Range("A1:F1").Style.Font.Bold = true;
            sheet.Columns(1, Headers.Length).Width = 32;

            sheet.Cell(2, 1).Value = "FALL25";
            sheet.Cell(2, 2).Value = "Teammy";
            sheet.Cell(2, 3).Value = "Giải pháp số quản lý nhóm";
            sheet.Cell(2, 4).Value = "open";
            sheet.Cell(2, 5).Value = sampleMajor1;
            sheet.Cell(2, 6).Value = "mentor1@example.com;mentor2@example.com";

            sheet.Cell(3, 1).Value = "SPRING26";
            sheet.Cell(3, 2).Value = "IoT Sensor Hub";
            sheet.Cell(3, 3).Value = "Gateway thu thập dữ liệu";
            sheet.Cell(3, 4).Value = "closed";
            sheet.Cell(3, 5).Value = sampleMajor2;
            sheet.Cell(3, 6).Value = "mentor@example.com";

            workbook.SaveAs(workbookStream);
        }

        workbookStream.Position = 0;
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = archive.CreateEntry("Topics.xlsx", CompressionLevel.SmallestSize);
            await using (var entryStream = sheetEntry.Open())
            {
                workbookStream.Position = 0;
                await workbookStream.CopyToAsync(entryStream, ct);
            }

            var docsEntry = archive.CreateEntry("docs/README.txt", CompressionLevel.Fastest);
            await using (var writer = new StreamWriter(docsEntry.Open(), Encoding.UTF8, leaveOpen: false))
            {
                await writer.WriteLineAsync("1. Giữ file metadata ở gốc (Topics.xlsx).");
                await writer.WriteLineAsync("2. Đặt tất cả file đăng ký topic (.docx/.txt) trong thư mục docs/.");
                await writer.WriteLineAsync("3. Đặt tên file đúng Title hoặc theo chuẩn <SemesterCode>_<Title>_<Owner>.docx (vd: FA25SE092_Teammy - A Digital Solution..._TruongLV11.docx). Hệ thống sẽ tự ánh xạ, không cần cột Source.");
            }
        }

        zipStream.Position = 0;
        return zipStream.ToArray();
    }

    public async Task<TopicImportResult> ImportAsync(Stream packageStream, Guid performedBy, CancellationToken ct)
    {
        using var packageBuffer = new MemoryStream();
        await packageStream.CopyToAsync(packageBuffer, ct);
        packageBuffer.Position = 0;

        using var archive = new ZipArchive(packageBuffer, ZipArchiveMode.Read, leaveOpen: false);
        var workbookEntry = FindWorkbookEntry(archive)
            ?? throw new InvalidOperationException("ZIP phải chứa file Topics.xlsx mô tả metadata.");

        using var workbookBuffer = new MemoryStream();
        await using (var workbookStream = workbookEntry.Open())
        {
            await workbookStream.CopyToAsync(workbookBuffer, ct);
        }
        workbookBuffer.Position = 0;

        using var workbook = new XLWorkbook(workbookBuffer);
        var sheet = workbook.Worksheet("Topics");
        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var headerMap = BuildHeaderMap(sheet);
        int semesterColumn = GetColumnIndex(headerMap, TopicImportColumns.SemesterCode, 1);
        int titleColumn = GetColumnIndex(headerMap, TopicImportColumns.Title, 2);
        int descriptionColumn = GetColumnIndex(headerMap, TopicImportColumns.Description, 3);
        int statusColumn = GetColumnIndex(headerMap, TopicImportColumns.Status, 4);
        int majorColumn = GetColumnIndex(headerMap, TopicImportColumns.MajorName, 5);
        int mentorColumn = GetColumnIndex(headerMap, TopicImportColumns.MentorEmails, 6);
        int registrationColumn = headerMap.TryGetValue(LegacyRegistrationHeader, out var legacyIndex) ? legacyIndex : -1;

        var documents = BuildDocumentLookup(archive, workbookEntry);
        var skillTokens = await LoadSkillDictionaryAsync(ct);

        int total = 0, created = 0, updated = 0, skipped = 0;
        var errors = new List<string>();
        var seenTopicKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int row = 2; row <= lastRow; row++)
        {
            ct.ThrowIfCancellationRequested();

            string rawSemester = sheet.Cell(row, semesterColumn).GetString().Trim();
            string title = sheet.Cell(row, titleColumn).GetString().Trim();
            string description = sheet.Cell(row, descriptionColumn).GetString().Trim();
            string registrationPath = registrationColumn > 0 ? sheet.Cell(row, registrationColumn).GetString().Trim() : string.Empty;
            string statusRaw = sheet.Cell(row, statusColumn).GetString().Trim();
            string majorName = sheet.Cell(row, majorColumn).GetString().Trim();
            string mentorRaw = sheet.Cell(row, mentorColumn).GetString().Trim();

            if (string.IsNullOrWhiteSpace(rawSemester) && string.IsNullOrWhiteSpace(title))
                continue;

            total++;

            if (string.IsNullOrWhiteSpace(rawSemester))
            {
                errors.Add($"Row {row}: SemesterCode required");
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                errors.Add($"Row {row}: Title required");
                skipped++;
                continue;
            }

            // Prevent duplicate topic title in the same semester within a single import package.
            var dupKey = $"{rawSemester.Trim()}|{title.Trim()}";
            if (!seenTopicKeys.Add(dupKey))
            {
                errors.Add($"Row {row}: Duplicate Title '{title}' in Semester '{rawSemester}' (not allowed)");
                skipped++;
                continue;
            }

            var (entry, entryError) = ResolveDocument(title, registrationPath, documents);
            if (entry is null)
            {
                errors.Add($"Row {row}: {entryError}");
                skipped++;
                continue;
            }

            if (!IsSupportedDocument(entry.Name))
            {
                errors.Add($"Row {row}: Registration file '{entry.Name}' must be DOCX or TXT.");
                skipped++;
                continue;
            }

            Guid semesterId;
            try
            {
                semesterId = await _semesters.EnsureByCodeAsync(rawSemester, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"Row {row}: Invalid SemesterCode '{rawSemester}' ({ex.Message})");
                skipped++;
                continue;
            }

            Guid? majorId = null;
            if (!string.IsNullOrWhiteSpace(majorName))
            {
                majorId = await _read.FindMajorIdByNameAsync(majorName, ct);
                if (majorId is null)
                {
                    errors.Add($"Row {row}: Major '{majorName}' not found");
                    skipped++;
                    continue;
                }
            }

            var status = string.IsNullOrWhiteSpace(statusRaw)
                ? "open"
                : statusRaw.Trim().ToLowerInvariant();

            if (!AllowedStatuses.Contains(status))
            {
                errors.Add($"Row {row}: Status must be open|closed|archived");
                skipped++;
                continue;
            }

            var mentorEmails = ParseMentorEmails(mentorRaw);
            var mentorIds = new List<Guid>();
            foreach (var email in mentorEmails)
            {
                try
                {
                    mentorIds.Add(await _mentorLookup.GetMentorIdByEmailAsync(email, ct));
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row}: Mentor '{email}' error: {ex.Message}");
                }
            }

            byte[] fileBytes;
            await using (var entryStream = entry.Open())
            {
                using var ms = new MemoryStream();
                await entryStream.CopyToAsync(ms, ct);
                fileBytes = ms.ToArray();
            }

            var skillTags = await BuildSkillTagsAsync(fileBytes, entry.Name, skillTokens, ct);

            await using var uploadStream = new MemoryStream(fileBytes);
            uploadStream.Position = 0;
            var uploadName = BuildUploadPath(rawSemester, entry.Name);
            var (fileUrl, fileType, fileSize) = await _fileStorage.SaveAsync(uploadStream, uploadName, ct);

            var (topicId, isCreated) = await _repo.UpsertAsync(
                semesterId,
                title,
                string.IsNullOrWhiteSpace(description) ? null : description,
                status,
                majorId,
                fileUrl,
                entry.Name,
                fileType,
                fileSize,
                skillTags,
                performedBy,
                ct);

            if (isCreated) created++; else updated++;

            await _topicMentors.ReplaceMentorsAsync(topicId, mentorIds, ct);
        }

        return new TopicImportResult(total, created, updated, skipped, errors);
    }

    public async Task<TopicImportValidationResult> ValidateRowsAsync(IReadOnlyList<TopicImportPayloadRow> rows, CancellationToken ct)
    {
        var safeRows = rows ?? Array.Empty<TopicImportPayloadRow>();
        var validations = new List<TopicImportRowValidation>(safeRows.Count);
        var semesterCache = new Dictionary<string, (bool Valid, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var majorCache = new Dictionary<string, (bool Valid, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var mentorCache = new Dictionary<string, (bool Valid, string? Error)>(StringComparer.OrdinalIgnoreCase);

        int validCount = 0, invalidCount = 0;
        var titleBySemester = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in safeRows)
        {
            ct.ThrowIfCancellationRequested();

            var columns = new List<TopicColumnValidation>(Headers.Length);
            var rowNumber = row.RowNumber > 0 ? row.RowNumber : validations.Count + 1;

            string semesterCode = row.SemesterCode?.Trim() ?? string.Empty;
            string title = row.Title?.Trim() ?? string.Empty;
            string description = row.Description?.Trim() ?? string.Empty;
            string status = row.Status?.Trim() ?? string.Empty;
            string majorName = row.MajorName?.Trim() ?? string.Empty;
            var mentorEmails = row.MentorEmails ?? Array.Empty<string>();

            bool rowEmpty = string.IsNullOrWhiteSpace(semesterCode)
                            && string.IsNullOrWhiteSpace(title)
                            && string.IsNullOrWhiteSpace(description)
                            && string.IsNullOrWhiteSpace(status)
                            && string.IsNullOrWhiteSpace(majorName)
                            && mentorEmails.All(string.IsNullOrWhiteSpace);

            if (rowEmpty)
            {
                const string emptyMessage = "Row is empty";
                foreach (var header in Headers)
                    columns.Add(new TopicColumnValidation(header, false, emptyMessage));
                validations.Add(new TopicImportRowValidation(rowNumber, false, columns, new[] { emptyMessage }));
                invalidCount++;
                continue;
            }

            var semesterValidation = ValidateSemester(semesterCode);
            columns.Add(new TopicColumnValidation(TopicImportColumns.SemesterCode, semesterValidation.Valid, semesterValidation.Error));

            bool titleValid = !string.IsNullOrWhiteSpace(title);
            string? titleError = titleValid ? null : "Title is required";
            if (titleValid && !string.IsNullOrWhiteSpace(semesterCode))
            {
                var key = $"{semesterCode}|{title}";
                if (!titleBySemester.Add(key))
                {
                    titleValid = false;
                    titleError = "Duplicate Title within the same SemesterCode";
                }
            }
            columns.Add(new TopicColumnValidation(TopicImportColumns.Title, titleValid, titleError));
            columns.Add(new TopicColumnValidation(TopicImportColumns.Description, true, null));

            bool statusValid = true;
            string? statusError = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.ToLowerInvariant();
                if (!AllowedStatuses.Contains(normalized))
                {
                    statusValid = false;
                    statusError = "Status must be open, closed, or archived";
                }
            }
            columns.Add(new TopicColumnValidation(TopicImportColumns.Status, statusValid, statusError));

            var majorValidation = await EnsureMajorAsync(majorName);
            columns.Add(new TopicColumnValidation(TopicImportColumns.MajorName, majorValidation.Valid, majorValidation.Error));

            var mentorErrors = new List<string>();
            var mentorSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool mentorsValid = true;

            foreach (var raw in mentorEmails)
            {
                var email = raw?.Trim();
                if (string.IsNullOrWhiteSpace(email))
                    continue;

                if (!mentorSet.Add(email))
                {
                    mentorsValid = false;
                    mentorErrors.Add($"Duplicate mentor email '{email}'");
                    continue;
                }

                if (!IsValidEmail(email))
                {
                    mentorsValid = false;
                    mentorErrors.Add($"Mentor email '{email}' format is invalid");
                    continue;
                }

                var validation = await EnsureMentorAsync(email);

                if (!validation.Valid)
                {
                    mentorsValid = false;
                    mentorErrors.Add(validation.Error ?? $"Mentor '{email}' not found");
                }
            }

            columns.Add(new TopicColumnValidation(
                TopicImportColumns.MentorEmails,
                mentorsValid,
                mentorErrors.Count == 0 ? null : string.Join("; ", mentorErrors)));

            var rowValid = columns.All(c => c.IsValid);
            if (rowValid) validCount++; else invalidCount++;

            validations.Add(new TopicImportRowValidation(rowNumber, rowValid, columns, Array.Empty<string>()));
        }

        var summary = new TopicValidationSummary(safeRows.Count, validCount, invalidCount);
        return new TopicImportValidationResult(summary, validations);

        (bool Valid, string? Error) ValidateSemester(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return (false, "SemesterCode is required");

            if (semesterCache.TryGetValue(code, out var cached))
                return cached;

            try
            {
                _ = SemesterCode.Parse(code);
                semesterCache[code] = (true, null);
            }
            catch (Exception ex)
            {
                semesterCache[code] = (false, ex.Message);
            }

            return semesterCache[code];
        }

        async Task<(bool Valid, string? Error)> EnsureMajorAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return (true, null);

            if (majorCache.TryGetValue(name, out var cached))
                return cached;

            var id = await _read.FindMajorIdByNameAsync(name, ct);
            var result = id is null
                ? (false, $"Major '{name}' not found")
                : (true, (string?)null);
            majorCache[name] = result;
            return result;
        }

        async Task<(bool Valid, string? Error)> EnsureMentorAsync(string email)
        {
            if (mentorCache.TryGetValue(email, out var cached))
                return cached;

            try
            {
                await _mentorLookup.GetMentorIdByEmailAsync(email, ct);
                mentorCache[email] = (true, null);
            }
            catch (Exception ex)
            {
                mentorCache[email] = (false, ex.Message);
            }

            return mentorCache[email];
        }
    }

    private static ZipArchiveEntry? FindWorkbookEntry(ZipArchive archive)
        => archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "Topics.xlsx", StringComparison.OrdinalIgnoreCase))
           ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));

    private static DocumentLookup BuildDocumentLookup(ZipArchive archive, ZipArchiveEntry workbook)
    {
        var byPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var byTitle = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (entry == workbook)
                continue;
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;
            if (IsMacMetadata(entry))
                continue;
            if (!IsSupportedDocument(entry.Name))
                continue;

            var normalized = NormalizePath(entry.FullName);
            byPath[normalized] = entry;

            if (!byPath.ContainsKey(entry.Name))
                byPath[entry.Name] = entry;

            var titleKey = NormalizeDocumentKey(entry.Name);
            if (string.IsNullOrWhiteSpace(titleKey))
                continue;

            if (!byTitle.TryGetValue(titleKey, out var entries))
            {
                entries = new List<ZipArchiveEntry>();
                byTitle[titleKey] = entries;
            }

            entries.Add(entry);
        }

        return new DocumentLookup(byPath, byTitle);
    }

    private static (ZipArchiveEntry? Entry, string? Error) ResolveDocument(string title, string? rawPath, DocumentLookup documents)
    {
        if (!string.IsNullOrWhiteSpace(rawPath))
            return ResolveDocumentByPath(rawPath, documents.ByPath);

        if (string.IsNullOrWhiteSpace(title))
            return (null, "Title is required to match a registration document");

        var titleKey = NormalizeDocumentKey(title);
        if (string.IsNullOrWhiteSpace(titleKey))
            return (null, $"Unable to build a file name from Title '{title}'");

        var candidates = FindDocumentCandidates(titleKey, documents.ByTitle);
        if (candidates.Count == 0)
        {
            return (null,
                $"Registration document for '{title}' was not found. Name the file exactly like the Title or follow the pattern <SemesterCode>_<Title>_<Owner>.docx (e.g., FA25SE092_Teammy - A Digital Solution..._TruongLV11.docx).");
        }

        if (candidates.Count > 1)
            return (null, $"Multiple registration documents match Title '{title}'. Ensure only one file uses that naming pattern.");

        return (candidates[0], null);
    }

    private static (ZipArchiveEntry? Entry, string? Error) ResolveDocumentByPath(string rawPath, IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var normalized = NormalizePath(rawPath);
        if (entries.TryGetValue(normalized, out var entry))
            return (entry, null);

        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(fileName) && entries.TryGetValue(fileName, out entry))
            return (entry, null);

        return (null, $"Registration file '{rawPath}' was not found inside the ZIP.");
    }

    private static IReadOnlyList<ZipArchiveEntry> FindDocumentCandidates(
        string titleKey,
        IReadOnlyDictionary<string, List<ZipArchiveEntry>> byTitle)
    {
        var results = new List<ZipArchiveEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddEntries(IEnumerable<ZipArchiveEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (seen.Add(entry.FullName))
                    results.Add(entry);
            }
        }

        if (byTitle.TryGetValue(titleKey, out var exactMatches) && exactMatches.Count > 0)
            AddEntries(exactMatches);

        if (results.Count > 0)
            return results;

        foreach (var pair in byTitle)
        {
            if (pair.Key.Contains(titleKey, StringComparison.Ordinal) || titleKey.Contains(pair.Key, StringComparison.Ordinal))
                AddEntries(pair.Value);
        }

        return results;
    }

    private async Task<IReadOnlyList<string>> BuildSkillTagsAsync(
        byte[] data,
        string fileName,
        IReadOnlyList<SkillDictionaryToken> dictionary,
        CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName);
        var text = ExtractDocumentText(data, extension);
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var dictionaryMatches = ExtractSkillTagsFromDictionary(text, dictionary);

        // Topic import is an interactive HTTP request; avoid long-running LLM calls that can cause reverse-proxy timeouts.
        // If dictionary heuristics already found enough signals, skip AI extraction.
        if (dictionaryMatches.Count >= 18)
            return dictionaryMatches;

        IReadOnlyList<string> aiMatches = Array.Empty<string>();

        try
        {
            aiMatches = await SkillExtractionPipeline.ExtractSkillsAsync(
                _llmClient,
                "topic_registration",
                Guid.NewGuid(),
                text,
                ct,
                chunkSize: 3500,
                maxChunks: 2,
                perChunkTimeout: TimeSpan.FromSeconds(8),
                totalTimeout: TimeSpan.FromSeconds(15));
        }
        catch
        {
            // ignore extraction failures; dictionary matches still returned
        }

        return MergeSkillTags(dictionaryMatches, aiMatches);
    }

    private static IReadOnlyList<string> ExtractSkillTagsFromDictionary(string text, IReadOnlyList<SkillDictionaryToken> dictionary)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var normalizedText = NormalizeForMatching(text);
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in dictionary)
        {
            if (ContainsToken(normalizedText, token.NormalizedToken))
            {
                matches.Add(token.DisplayToken);
                continue;
            }

            foreach (var alias in token.Aliases)
            {
                if (ContainsToken(normalizedText, alias))
                {
                    matches.Add(token.DisplayToken);
                    break;
                }
            }
        }

        foreach (var token in ExtractTechnologyTokens(text))
        {
            if (matches.Count >= 30)
                break;
            matches.Add(token);
        }

        return matches.Count == 0 ? Array.Empty<string>() : matches.Take(30).ToList();
    }

    private static IReadOnlyList<string> MergeSkillTags(IReadOnlyList<string> dictionaryMatches, IReadOnlyList<string> aiMatches)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in dictionaryMatches)
        {
            if (!string.IsNullOrWhiteSpace(skill))
                merged.Add(skill.Trim());
        }

        foreach (var skill in aiMatches)
        {
            if (!string.IsNullOrWhiteSpace(skill))
                merged.Add(skill.Trim());
        }

        return merged.Count == 0 ? Array.Empty<string>() : merged.Take(40).ToList();
    }

    private async Task<IReadOnlyList<SkillDictionaryToken>> LoadSkillDictionaryAsync(CancellationToken ct)
    {
        var rows = await _skillDictionary.ListAsync(null, null, ct);
        return rows
            .Select(r => new SkillDictionaryToken(
                r.Token,
                NormalizeForMatching(r.Token),
                r.Aliases?
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(NormalizeForMatching)
                    .Where(a => a.Length > 1)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>()))
            .ToList();
    }

    private static string ExtractDocumentText(byte[] buffer, string? extension)
    {
        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetString(buffer);

        if (!string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported registration file format. Only DOCX and TXT are allowed.");

        using var stream = new MemoryStream(buffer, writable: false);
        using var doc = WordprocessingDocument.Open(stream, false);
        return doc.MainDocumentPart?.Document?.InnerText ?? string.Empty;
    }

    private static IEnumerable<string> ExtractTechnologyTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return;

            foreach (var raw in source.Split(SkillTokenSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var variant in ExpandTokenVariants(raw))
                {
                    var normalized = SkillTaxonomy.TryNormalize(variant);
                    if (normalized is not null)
                        collected.Add(normalized);
                }
            }
        }

        var section = ExtractSection(text, "Technologies", "Products", "Proposed", "Functional requirements", "Task", "Skills", "Stack");
        Collect(section);
        Collect(text);

        return collected;
    }

    private static IEnumerable<string> ExpandTokenVariants(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        yield return raw;

        foreach (Match match in Regex.Matches(raw, @"\(([^)]{1,60})\)"))
        {
            var inside = match.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(inside))
                yield return inside;
        }

        var withoutParens = Regex.Replace(raw, @"\([^)]*\)", " ");
        if (!string.Equals(withoutParens, raw, StringComparison.Ordinal))
            yield return withoutParens;
    }

    private static string ExtractSection(string text, string startMarker, params string[] endMarkers)
    {
        var start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;
        start += startMarker.Length;
        var end = text.Length;
        foreach (var endMarker in endMarkers)
        {
            var idx = text.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < end)
                end = idx;
        }
        if (end <= start)
            return string.Empty;
        return text[start..end];
    }

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return false;
        return Regex.IsMatch(haystack, $"\\b{Regex.Escape(needle)}\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string BuildUploadPath(string semesterCode, string fileName)
    {
        var season = string.IsNullOrWhiteSpace(semesterCode) ? "unknown" : semesterCode.Trim();
        return $"topics/{season}/{fileName}";
    }

    private static bool IsSupportedDocument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var ext = Path.GetExtension(path);
        return SupportedExtensions.Contains(ext ?? string.Empty);
    }

    private static string NormalizePath(string raw)
    {
        var replaced = raw.Replace('\\', '/');
        var trimmed = replaced.Trim('/');
        return trimmed;
    }

    private static string NormalizeDocumentKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var name = Path.GetFileNameWithoutExtension(value);
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalized = NormalizeForMatching(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string NormalizeForMatching(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static bool IsValidEmail(string email)
        => System.Net.Mail.MailAddress.TryCreate(email, out _);

    private static List<string> ParseMentorEmails(string raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastColumn = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? Headers.Length;
        for (int col = 1; col <= lastColumn; col++)
        {
            var header = sheet.Cell(1, col).GetString();
            if (!string.IsNullOrWhiteSpace(header))
                map[header.Trim()] = col;
        }

        return map;
    }

    private static int GetColumnIndex(IReadOnlyDictionary<string, int> headers, string column, int fallback)
        => headers.TryGetValue(column, out var index) ? index : fallback;

    private sealed record SkillDictionaryToken(string DisplayToken, string NormalizedToken, IReadOnlyList<string> Aliases);

    private sealed record DocumentLookup(
        IReadOnlyDictionary<string, ZipArchiveEntry> ByPath,
        IReadOnlyDictionary<string, List<ZipArchiveEntry>> ByTitle);

    private static bool IsMacMetadata(ZipArchiveEntry entry)
    {
        if (entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (entry.Name.StartsWith("._", StringComparison.Ordinal))
            return true;
        if (string.Equals(entry.Name, ".DS_Store", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static class SkillTaxonomy
    {
        private static readonly SkillEntry[] Entries =
        {
            new("API Design", "api", "rest api", "restful api", "web api"),
            new("GraphQL"),
            new("gRPC", "grpc"),
            new("Microservices", "micro-service"),
            new("Clean Architecture"),
            new("Domain-Driven Design", "ddd"),
            new(".NET", ".net", "dotnet"),
            new(".NET Core", ".netcore", "dotnetcore", ".net core"),
            new("ASP.NET", "aspnet"),
            new("C#", "csharp", "c#"),
            new("Java"),
            new("Python"),
            new("JavaScript", "js"),
            new("TypeScript", "ts"),
            new("Go", "golang"),
            new("Rust"),
            new("PHP"),
            new("Ruby"),
            new("Swift"),
            new("Kotlin"),
            new("Dart"),
            new("Scala"),
            new("C++", "cpp"),
            new("Node.js", "node", "nodejs"),
            new("Express.js", "express"),
            new("NestJS", "nest"),
            new("Next.js", "nextjs"),
            new("Nuxt", "nuxtjs"),
            new("React", "reactjs", "react.js"),
            new("React Native", "reactnative"),
            new("Angular"),
            new("Vue", "vuejs"),
            new("Svelte"),
            new("Blazor"),
            new("Tailwind CSS", "tailwind"),
            new("Sass"),
            new("Bootstrap"),
            new("HTML", "html5"),
            new("CSS", "css3"),
            new("Flutter"),
            new("Android"),
            new("iOS", "ios"),
            new("Xamarin"),
            new(".NET MAUI", "maui"),
            new("SQL"),
            new("MySQL"),
            new("PostgreSQL", "postgres", "postgresql"),
            new("MongoDB", "mongo"),
            new("Redis"),
            new("Elasticsearch", "elastic"),
            new("Cassandra"),
            new("Oracle"),
            new("SQL Server", "mssql"),
            new("Kafka"),
            new("Spark"),
            new("Hadoop"),
            new("Airflow"),
            new("dbt"),
            new("ETL"),
            new("Data Warehouse", "datawarehouse"),
            new("Power BI", "powerbi"),
            new("Tableau"),
            new("Looker"),
            new("Snowflake"),
            new("BigQuery"),
            new("Machine Learning", "ml", "machinelearning"),
            new("Deep Learning", "deeplearning"),
            new("NLP"),
            new("Computer Vision"),
            new("TensorFlow"),
            new("PyTorch"),
            new("scikit-learn", "scikitlearn"),
            new("AWS"),
            new("Azure"),
            new("GCP", "google cloud"),
            new("Firebase"),
            new("Supabase"),
            new("Docker"),
            new("Kubernetes", "k8s"),
            new("CI/CD", "cicd"),
            new("GitHub Actions", "githubactions"),
            new("GitLab CI", "gitlabci"),
            new("Jenkins"),
            new("Terraform"),
            new("Ansible"),
            new("Helm"),
            new("Cloud Hosting", "cloudhosting", "cloud hosting"),
            new("Figma"),
            new("Sketch"),
            new("Adobe XD", "xd"),
            new("Photoshop"),
            new("Illustrator"),
            new("After Effects", "aftereffects"),
            new("Blender"),
            new("UI/UX", "uiux"),
            new("Wireframing"),
            new("Prototyping"),
            new("Motion Design", "motiondesign"),
            new("Content Marketing", "contentmarketing"),
            new("SEO"),
            new("SEM"),
            new("Social Media", "socialmedia"),
            new("Growth Marketing", "growthmarketing"),
            new("Email Marketing", "emailmarketing"),
            new("Copywriting"),
            new("Brand Strategy", "brandstrategy"),
            new("Agile"),
            new("Scrum"),
            new("Kanban"),
            new("Project Management", "projectmanagement"),
            new("Product Management", "productmanagement"),
            new("Leadership"),
            new("Communication"),
            new("Teamwork"),
            new("Collaboration"),
            new("Presentation"),
            new("Problem Solving", "problemsolving"),
            new("Critical Thinking", "criticalthinking"),
            new("Time Management", "timemanagement"),
            new("Adaptability"),
            new("Creativity"),
            new("Negotiation"),
            new("Public Speaking", "publicspeaking"),
            new("Conflict Resolution", "conflictresolution"),
            new("REST", "rest"),
            new("Graph Databases", "graphdb", "neo4j"),
            new("Cybersecurity", "security"),
            new("QA Automation", "testautomation", "automation testing"),
            new("Unit Testing", "unittesting"),
            new("Integration Testing", "integrationtesting")
        };

        private static readonly Dictionary<string, string> CanonicalByKey = BuildCanonicalMap();
        private static readonly char[] TrimChars = { '-', '–', '—', ':', ';', '.', ',', '"', '\'', '•', '*', '·', '/', '\\', '(', ')', '[', ']', '{', '}' };
        private static readonly char[] BulletPrefixes = { '•', '○', '●', '◦', '*', '-', '–', '—' };

        public static string? TryNormalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var trimmed = raw.Trim().Trim(TrimChars);
            trimmed = TrimBulletPrefix(trimmed);
            if (trimmed.Length >= 3)
            {
                var colonIdx = trimmed.LastIndexOf(':');
                if (colonIdx >= 0 && colonIdx < trimmed.Length - 1)
                    trimmed = trimmed[(colonIdx + 1)..].Trim(TrimChars);
            }

            if (trimmed.Length < 2)
                return null;

            if (trimmed.StartsWith("and ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[4..].Trim();
            if (trimmed.EndsWith(" etc", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^3].Trim();

            if (trimmed.Length < 2)
                return null;

            var key = Canonicalize(trimmed);
            if (key.Length < 2)
                return null;

            return CanonicalByKey.TryGetValue(key, out var canonical) ? canonical : null;
        }

        private static string TrimBulletPrefix(string value)
        {
            var span = value.AsSpan().TrimStart();

            while (span.Length > 1)
            {
                if (BulletPrefixes.Contains(span[0]) && char.IsWhiteSpace(span[1]))
                {
                    span = span[1..].TrimStart();
                    continue;
                }

                if ((span[0] == 'o' || span[0] == 'O') && char.IsWhiteSpace(span[1]))
                {
                    span = span[1..].TrimStart();
                    continue;
                }

                break;
            }

            return span.ToString();
        }

        private static Dictionary<string, string> BuildCanonicalMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Entries)
            {
                foreach (var variant in entry.GetVariants())
                {
                    var key = Canonicalize(variant);
                    if (key.Length == 0)
                        continue;
                    if (!map.ContainsKey(key))
                        map[key] = entry.Canonical;
                }
            }
            return map;
        }

        private static string Canonicalize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(char.ToLowerInvariant(ch));
            }
            return builder.ToString();
        }

        private sealed record SkillEntry(string Canonical, params string[] Variants)
        {
            public IEnumerable<string> GetVariants()
            {
                yield return Canonical;
                if (Variants is null)
                    yield break;
                foreach (var variant in Variants)
                    yield return variant;
            }
        }
    }
}
