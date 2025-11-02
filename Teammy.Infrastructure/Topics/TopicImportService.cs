using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces.Topics;
using Teammy.Infrastructure.Models;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Topics;

public sealed class TopicImportService : ITopicImportService
{
    private readonly AppDbContext _db;
    public TopicImportService(AppDbContext db) => _db = db;

    public async Task<TopicImportResult> ImportAsync(Guid termId, Stream fileStream, string fileName, Guid actorId, CancellationToken ct, Guid? majorId = null)
    {
        if (termId == Guid.Empty) throw new ArgumentException("termId required");
        if (fileStream is null || !fileStream.CanRead) throw new ArgumentException("file stream invalid");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".xlsx")
            throw new InvalidOperationException("Only .xlsx files are supported for import");

        var rows = ReadXlsxRows(fileStream);
        if (rows.Count == 0)
            return await LogAndReturnAsync(termId, fileName, actorId, 0, 0, 0, Array.Empty<object>(), ct);

        var headers = rows[0].Select(h => (h ?? string.Empty).Trim()).ToList();
        int idxTitle = FindHeader(headers, "title");
        int idxCode = FindHeader(headers, "code");
        int idxDesc = FindHeader(headers, "description");

        if (idxTitle < 0)
            throw new InvalidOperationException("Missing 'Title' column in the first row");
        var existingTitles = await _db.topics
            .Where(t => t.term_id == termId)
            .Select(t => t.title)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingTitles, StringComparer.OrdinalIgnoreCase);

        int total = 0, ok = 0, err = 0;
        var errorList = new List<object>();
        var toAdd = new List<topic>();

        for (int i = 1; i < rows.Count; i++)
        {
            total++;
            var row = rows[i];
            string title = SafeGet(row, idxTitle);
            string code = idxCode >= 0 ? SafeGet(row, idxCode) : string.Empty;
            string description = idxDesc >= 0 ? SafeGet(row, idxDesc) : string.Empty;

            if (string.IsNullOrWhiteSpace(title))
            {
                err++;
                errorList.Add(new { row = i + 1, error = "Title is required" });
                continue;
            }

            if (existingSet.Contains(title))
            {
                err++;
                errorList.Add(new { row = i + 1, error = "Duplicate title in this term" });
                continue;
            }

            if (toAdd.Any(t => string.Equals(t.title, title, StringComparison.OrdinalIgnoreCase)))
            {
                err++;
                errorList.Add(new { row = i + 1, error = "Duplicate title within file" });
                continue;
            }

            toAdd.Add(new topic
            {
                term_id = termId,
                code = string.IsNullOrWhiteSpace(code) ? null : code,
                title = title,
                description = string.IsNullOrWhiteSpace(description) ? null : description,
                created_by = actorId,
                status = "open",
                major_id = majorId
            });
            ok++;
            existingSet.Add(title);
        }

        if (toAdd.Count > 0)
        {
            await _db.topics.AddRangeAsync(toAdd, ct);
            await _db.SaveChangesAsync(ct);
        }

        return await LogAndReturnAsync(termId, fileName, actorId, total, ok, err, errorList, ct);
    }

    private async Task<TopicImportResult> LogAndReturnAsync(Guid termId, string fileName, Guid actorId, int total, int ok, int err, IEnumerable<object> errors, CancellationToken ct)
    {
        var job = new topic_import_job
        {
            term_id = termId,
            file_name = fileName,
            total_rows = total,
            success_rows = ok,
            error_rows = err,
            status = "completed",
            errors = JsonSerializer.Serialize(errors)
        };
        job.created_by = actorId;

        _db.topic_import_jobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return new TopicImportResult(job.id, total, ok, err);
    }

    private static int FindHeader(IReadOnlyList<string> headers, string key)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string SafeGet(IReadOnlyList<string?> row, int index)
    {
        if (index < 0 || index >= row.Count) return string.Empty;
        return row[index] ?? string.Empty;
    }

    private static List<List<string?>> ReadXlsxRows(Stream xlsx)
    {
        using var ms = new MemoryStream();
        xlsx.CopyTo(ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = new List<string>();
        var sst = archive.GetEntry("xl/sharedStrings.xml");
        if (sst != null)
        {
            using var sstStream = sst.Open();
            using var xr = XmlReader.Create(sstStream, new XmlReaderSettings { IgnoreWhitespace = true });
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.Name == "si")
                {
                    sharedStrings.Add(ReadStringItem(xr.ReadSubtree()));
                }
            }
        }

        var sheetPath = ResolveFirstWorksheetPath(archive) ?? "xl/worksheets/sheet1.xml";
        var sheetEntry = archive.GetEntry(sheetPath) ?? archive.GetEntry("xl/worksheets/sheet1.xml");
        if (sheetEntry == null) return new List<List<string?>>();

        var rows = new List<List<string?>>();
        using var ws = sheetEntry.Open();
        using var r = XmlReader.Create(ws, new XmlReaderSettings { IgnoreWhitespace = true });

        int currentColIndex = 0;
        List<string?>? currentRow = null;

        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.Name == "row")
            {
                currentRow = new List<string?>();
                currentColIndex = 0;
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.Name == "row")
            {
                if (currentRow != null) rows.Add(currentRow);
                currentRow = null;
            }
            else if (r.NodeType == XmlNodeType.Element && r.Name == "c" && currentRow != null)
            {
                var cellType = r.GetAttribute("t");
                var cellRef = r.GetAttribute("r");
                int colIdx = ColumnIndexFromCellRef(cellRef);

                while (currentRow.Count < colIdx) currentRow.Add(null);

                string? value = null;
                if (!r.IsEmptyElement)
                {
                    using var csub = r.ReadSubtree();
                    value = ReadCellValue(csub, cellType, sharedStrings);
                }

                if (currentRow.Count == colIdx) currentRow.Add(value);
                else currentRow[colIdx] = value;
                currentColIndex = colIdx + 1;
            }
        }

        return rows;
    }

    private static string ReadStringItem(XmlReader xr)
    {
        var sb = new StringBuilder();
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.Name == "t")
            {
                var text = xr.ReadElementContentAsString();
                sb.Append(text);
            }
        }
        return sb.ToString();
    }

    private static string? ReadCellValue(XmlReader xr, string? type, List<string> sharedStrings)
    {
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.Name == "v")
            {
                var raw = xr.ReadElementContentAsString();
                if (type == "s")
                {
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) && idx >= 0 && idx < sharedStrings.Count)
                        return sharedStrings[idx];
                    return string.Empty;
                }
                return raw;
            }
            if (xr.NodeType == XmlNodeType.Element && xr.Name == "is")
            {
                return ReadStringItem(xr.ReadSubtree());
            }
        }
        return null;
    }
    private static int ColumnIndexFromCellRef(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        int i = 0;
        int col = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
            i++;
        }
        return col - 1 < 0 ? 0 : col - 1;
    }

    private static string? ResolveFirstWorksheetPath(ZipArchive archive)
    {
        var workbook = archive.GetEntry("xl/workbook.xml");
        var rels = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbook == null || rels == null) return null;

        var sheetRelId = (string?)null;
        using (var xr = XmlReader.Create(workbook.Open(), new XmlReaderSettings { IgnoreWhitespace = true }))
        {
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.Name == "sheet")
                {
                    sheetRelId = xr.GetAttribute("r:id");
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(sheetRelId)) return null;

        using (var xr = XmlReader.Create(rels.Open(), new XmlReaderSettings { IgnoreWhitespace = true }))
        {
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.Name.EndsWith("Relationship", StringComparison.Ordinal))
                {
                    var id = xr.GetAttribute("Id");
                    if (id == sheetRelId)
                    {
                        var target = xr.GetAttribute("Target");
                        if (!string.IsNullOrEmpty(target))
                        {
                            return "xl/" + target.Replace("\\", "/");
                        }
                    }
                }
            }
        }
        return null;
    }
}
