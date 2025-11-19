using System.Net.Mail;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Users.Import.Dtos;

namespace Teammy.Infrastructure.Excel;

public sealed class ExcelUserImportService(
    IUserWriteRepository userWriteRepo,
    IRoleReadOnlyQueries roleQueries,
    IMajorReadOnlyQueries majorQueries
) : IUserImportService
{
    private static readonly string[] Headers = { "Email","DisplayName","Role","MajorName","Gender","StudentCode" };
    private static readonly HashSet<string> AllowedGenders = new(StringComparer.OrdinalIgnoreCase)
    {
        "male",
        "female",
        "other"
    };

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct)
    {
        var roles  = await roleQueries.GetAllRoleNamesAsync(ct);
        var majors = await majorQueries.GetAllMajorNamesAsync(ct);
        var genders = new[] { "male", "female", "other" };

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Users");
        var lookup = wb.AddWorksheet("Lookups");

        // Header
        for (int i = 0; i < Headers.Length; i++)
            ws.Cell(1, i + 1).Value = Headers[i];

        ws.Range("A1:F1").Style.Font.Bold = true;
        ws.Columns(1, 6).Width = 28;

        // Sample rows
        ws.Cell(2, 1).Value = "alice@example.com";
        ws.Cell(2, 2).Value = "Alice Nguyen";
        ws.Cell(2, 3).Value = roles.FirstOrDefault() ?? "student";
        ws.Cell(2, 4).Value = majors.FirstOrDefault() ?? "";
        ws.Cell(2, 5).Value = "female";
        ws.Cell(2, 6).Value = "SE150001";

        ws.Cell(3, 1).Value = "bob@example.com";
        ws.Cell(3, 2).Value = "Bob Tran";
        ws.Cell(3, 3).Value = roles.FirstOrDefault() ?? "student";
        ws.Cell(3, 4).Value = "";
        ws.Cell(3, 5).Value = "male";
        ws.Cell(3, 6).Value = "";

        // Lookups
        lookup.Cell(1,1).Value = "Roles";
        for (int i = 0; i < roles.Count; i++) lookup.Cell(i+2, 1).Value = roles[i];

        lookup.Cell(1,3).Value = "Majors";
        for (int i = 0; i < majors.Count; i++) lookup.Cell(i+2, 3).Value = majors[i];

        lookup.Cell(1,5).Value = "Genders";
        for (int i = 0; i < genders.Length; i++) lookup.Cell(i+2, 5).Value = genders[i];

        // Named ranges
        var rolesRange   = lookup.Range(2,1, roles.Count + 1, 1); rolesRange.AddToNamed("RolesRange");
        var majorsRange  = lookup.Range(2,3, majors.Count + 1,3); majorsRange.AddToNamed("MajorsRange");
        var gendersRange = lookup.Range(2,5, genders.Length + 1,5); gendersRange.AddToNamed("GendersRange");

        // Data validations
        ws.Range("C2:C10000").CreateDataValidation().List("=RolesRange", true);
        ws.Range("D2:D10000").CreateDataValidation().List("=MajorsRange", false);
        ws.Range("E2:E10000").CreateDataValidation().List("=GendersRange", false);

        // Required markers
        ws.Range("A2:A10000").Style.Fill.BackgroundColor = XLColor.LightYellow; // Email required
        ws.Range("B2:B10000").Style.Fill.BackgroundColor = XLColor.LightYellow; // DisplayName required
        ws.Range("C2:C10000").Style.Fill.BackgroundColor = XLColor.LightYellow; // Role required

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<ImportUsersResult> ImportAsync(Stream excelStream, Guid performedByUserId, CancellationToken ct)
    {
        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheet("Users");

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var rows = new List<ImportUserRow>();
        var errors = new List<ImportUsersError>();

        // Read rows
        for (int r = 2; r <= lastRow; r++)
        {
            string email = ws.Cell(r,1).GetString().Trim();
            string name  = ws.Cell(r,2).GetString().Trim();
            string role  = ws.Cell(r,3).GetString().Trim();
            string major = ws.Cell(r,4).GetString().Trim();
            string gender= ws.Cell(r,5).GetString().Trim();
            string code  = ws.Cell(r,6).GetString().Trim();

            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(name)) continue; // skip empty row

            // Validate core required
            if (string.IsNullOrWhiteSpace(email))
            {
                errors.Add(new ImportUsersError(r, "Email is required"));
                continue;
            }
            if (!IsValidEmail(email))
            {
                errors.Add(new ImportUsersError(r, "Email format is invalid"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportUsersError(r, "DisplayName is required"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(role))
            {
                errors.Add(new ImportUsersError(r, "Role is required"));
                continue;
            }

            rows.Add(new ImportUserRow(
                email,
                name,
                role,
                string.IsNullOrWhiteSpace(major) ? null : major,
                string.IsNullOrWhiteSpace(gender) ? null : gender.ToLowerInvariant(),
                string.IsNullOrWhiteSpace(code) ? null : code
            ));
        }

        // early exits
        if (rows.Count == 0)
            return new ImportUsersResult(0, 0, 0, errors);

        // Deduplicate by email inside file
        var firstByEmail = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filtered = new List<ImportUserRow>();
        foreach (var row in rows)
        {
            if (firstByEmail.Add(row.Email)) filtered.Add(row);
            else errors.Add(new ImportUsersError(-1, $"Duplicate email in file: {row.Email} (skipped)"));
        }

        int created = 0, skipped = 0;

        foreach (var row in filtered)
        {
            // Role map
            var roleId = await roleQueries.GetRoleIdByNameAsync(row.Role, ct);
            if (roleId is null)
            {
                errors.Add(new ImportUsersError(-1, $"Role not found: {row.Role} (email: {row.Email})"));
                skipped++;
                continue;
            }

            // Major map (optional)
            Guid? majorId = null;
            if (!string.IsNullOrWhiteSpace(row.MajorName))
            {
                majorId = await majorQueries.FindMajorIdByNameAsync(row.MajorName!, ct);
                if (majorId is null)
                {
                    errors.Add(new ImportUsersError(-1, $"Major not found: {row.MajorName} (email: {row.Email})"));
                    skipped++;
                    continue;
                }
            }

            // Exists?
            if (await userWriteRepo.EmailExistsAnyAsync(row.Email, ct))
            {
                skipped++;
                continue;
            }

            var userId = await userWriteRepo.CreateUserAsync(
                email: row.Email,
                displayName: row.DisplayName,
                studentCode: row.StudentCode,
                gender: row.Gender,
                majorId: majorId,
                ct: ct);

            await userWriteRepo.AssignRoleAsync(userId, roleId.Value, ct);
            created++;
        }

        return new ImportUsersResult(
            TotalRows: rows.Count,
            CreatedCount: created,
            SkippedCount: skipped,
            Errors: errors);
    }

    public async Task<UserImportValidationResult> ValidateRowsAsync(
        IReadOnlyList<UserImportPayloadRow> rows,
        CancellationToken ct)
    {
        var safeRows = rows ?? Array.Empty<UserImportPayloadRow>();
        var results = new List<UserImportRowValidation>(safeRows.Count);

        var emailFirstOccurrence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var emailExistsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var roleCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var majorCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        int validCount = 0, invalidCount = 0;

        foreach (var row in safeRows)
        {
            ct.ThrowIfCancellationRequested();

            var columns = new List<UserColumnValidation>(Headers.Length);
            var messages = new List<string>();

            var rowNumber = row is not null && row.RowNumber > 0
                ? row.RowNumber
                : results.Count + 1;

            string email = row?.Email?.Trim() ?? string.Empty;
            string displayName = row?.DisplayName?.Trim() ?? string.Empty;
            string role = row?.Role?.Trim() ?? string.Empty;
            string major = row?.MajorName?.Trim() ?? string.Empty;
            string gender = row?.Gender?.Trim() ?? string.Empty;
            string studentCode = row?.StudentCode?.Trim() ?? string.Empty;

            bool rowIsEmpty = string.IsNullOrWhiteSpace(email)
                              && string.IsNullOrWhiteSpace(displayName)
                              && string.IsNullOrWhiteSpace(role)
                              && string.IsNullOrWhiteSpace(major)
                              && string.IsNullOrWhiteSpace(gender)
                              && string.IsNullOrWhiteSpace(studentCode);

            if (rowIsEmpty)
            {
                const string emptyMessage = "Row is empty";
                columns.Add(new UserColumnValidation("Email", false, emptyMessage));
                columns.Add(new UserColumnValidation("DisplayName", false, emptyMessage));
                columns.Add(new UserColumnValidation("Role", false, emptyMessage));
                columns.Add(new UserColumnValidation("MajorName", false, emptyMessage));
                columns.Add(new UserColumnValidation("Gender", false, emptyMessage));
                columns.Add(new UserColumnValidation("StudentCode", false, emptyMessage));
                messages.Add("Row has no data");
                invalidCount++;
                results.Add(new UserImportRowValidation(rowNumber, false, columns, messages));
                continue;
            }

            bool emailValid = true;
            var emailErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(email))
            {
                emailValid = false;
                emailErrors.Add("Email is required");
            }
            else if (!IsValidEmail(email))
            {
                emailValid = false;
                emailErrors.Add("Email format is invalid");
            }
            else if (!emailFirstOccurrence.TryAdd(email, rowNumber))
            {
                emailValid = false;
                emailErrors.Add("Duplicate email within payload");
            }
            else if (await EmailExistsAsync(email, emailExistsCache, ct))
            {
                emailValid = false;
                emailErrors.Add("Email already exists in system");
            }

            columns.Add(new UserColumnValidation("Email", emailValid, emailValid ? null : string.Join("; ", emailErrors)));

            bool nameValid = !string.IsNullOrWhiteSpace(displayName);
            columns.Add(new UserColumnValidation("DisplayName", nameValid, nameValid ? null : "DisplayName is required"));

            bool roleValid = true;
            string? roleError = null;
            if (string.IsNullOrWhiteSpace(role))
            {
                roleValid = false;
                roleError = "Role is required";
            }
            else
            {
                if (!roleCache.TryGetValue(role, out var roleId))
                {
                    roleId = await roleQueries.GetRoleIdByNameAsync(role, ct);
                    roleCache[role] = roleId;
                }

                if (roleId is null)
                {
                    roleValid = false;
                    roleError = $"Role '{role}' not found";
                }
            }
            columns.Add(new UserColumnValidation("Role", roleValid, roleError));

            bool majorValid = true;
            string? majorError = null;
            if (!string.IsNullOrWhiteSpace(major))
            {
                if (!majorCache.TryGetValue(major, out var majorId))
                {
                    majorId = await majorQueries.FindMajorIdByNameAsync(major, ct);
                    majorCache[major] = majorId;
                }

                if (majorId is null)
                {
                    majorValid = false;
                    majorError = $"Major '{major}' not found";
                }
            }
            columns.Add(new UserColumnValidation("MajorName", majorValid, majorError));

            bool genderValid = true;
            string? genderError = null;
            if (!string.IsNullOrWhiteSpace(gender))
            {
                var normalizedGender = gender.ToLowerInvariant();
                if (!AllowedGenders.Contains(normalizedGender))
                {
                    genderValid = false;
                    genderError = "Gender must be male, female, or other";
                }
            }
            columns.Add(new UserColumnValidation("Gender", genderValid, genderError));
            bool studentCodeValid = string.IsNullOrWhiteSpace(studentCode) || studentCode.Length <= 30;
            string? studentCodeError = studentCodeValid ? null : "StudentCode must be <= 30 characters";
            columns.Add(new UserColumnValidation("StudentCode", studentCodeValid, studentCodeError));

            bool rowValid = columns.All(c => c.IsValid);
            if (rowValid) validCount++; else invalidCount++;

            results.Add(new UserImportRowValidation(rowNumber, rowValid, columns, messages));
        }

        var summary = new UsersValidationSummary(safeRows.Count, validCount, invalidCount);
        return new UserImportValidationResult(summary, results);

        async Task<bool> EmailExistsAsync(string normalizedEmail, Dictionary<string, bool> cache, CancellationToken token)
        {
            if (cache.TryGetValue(normalizedEmail, out var exists)) return exists;
            exists = await userWriteRepo.EmailExistsAnyAsync(normalizedEmail, token);
            cache[normalizedEmail] = exists;
            return exists;
        }
    }

    private static bool IsValidEmail(string email)
    {
        try { _ = new MailAddress(email); return true; }
        catch { return false; }
    }
}
