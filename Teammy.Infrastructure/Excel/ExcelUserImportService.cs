using System.Net.Mail;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Semesters.Dtos;
using Teammy.Application.Users.Import.Dtos;

namespace Teammy.Infrastructure.Excel;

public sealed class ExcelUserImportService(
    IUserReadOnlyQueries userReadOnlyQueries,
    IUserWriteRepository userWriteRepo,
    IRoleReadOnlyQueries roleQueries,
    IMajorReadOnlyQueries majorQueries,
    ISemesterReadOnlyQueries semesterQueries,
    IStudentSemesterReadOnlyQueries studentSemesterReadOnly,
    IStudentSemesterWriteRepository studentSemesterWrite,
    IEmailSender emailSender
) : IUserImportService
{
    private static readonly string[] Headers = { "Email","DisplayName","Role","MajorName","Gender","StudentCode","GPA","SemesterCode" };
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

        ws.Range("A1:H1").Style.Font.Bold = true;
        ws.Columns(1, 8).Width = 28;

        // Sample rows
        ws.Cell(2, 1).Value = "alice@example.com";
        ws.Cell(2, 2).Value = "Alice Nguyen";
        ws.Cell(2, 3).Value = roles.FirstOrDefault() ?? "student";
        ws.Cell(2, 4).Value = majors.FirstOrDefault() ?? "";
        ws.Cell(2, 5).Value = "female";
        ws.Cell(2, 6).Value = "SE150001";
        ws.Cell(2, 7).Value = 3.2;
        ws.Cell(2, 8).Value = "FALL2025";

        ws.Cell(3, 1).Value = "bob@example.com";
        ws.Cell(3, 2).Value = "Bob Tran";
        ws.Cell(3, 3).Value = roles.FirstOrDefault() ?? "student";
        ws.Cell(3, 4).Value = "";
        ws.Cell(3, 5).Value = "male";
        ws.Cell(3, 6).Value = "";
        ws.Cell(3, 7).Value = "";
        ws.Cell(3, 8).Value = "FA2025";

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
        var semesters = await semesterQueries.ListAsync(ct);

        // Read rows
        for (int r = 2; r <= lastRow; r++)
        {
            string email = ws.Cell(r,1).GetString().Trim();
            string name  = ws.Cell(r,2).GetString().Trim();
            string role  = ws.Cell(r,3).GetString().Trim();
            string major = ws.Cell(r,4).GetString().Trim();
            string gender= ws.Cell(r,5).GetString().Trim();
            string code  = ws.Cell(r,6).GetString().Trim();
            var gpaCell = ws.Cell(r, 7);
            string semesterCode = ws.Cell(r, 8).GetString().Trim();

            double? gpa = null;

            // GPA parsing notes:
            // - Excel numeric cells should be read as numbers (culture-independent)
            // - String cells may contain either decimal dot (3.5) or decimal comma (3,5)
            // - We must NOT parse decimal comma as thousands separator ("3,5" -> 35)
            if (!gpaCell.IsEmpty())
            {
                if (gpaCell.TryGetValue<double>(out var numericGpa))
                {
                    if (numericGpa < 0)
                    {
                        errors.Add(new ImportUsersError(r, "GPA must be >= 0"));
                        continue;
                    }

                    if (numericGpa > 10)
                    {
                        errors.Add(new ImportUsersError(r, "GPA must be <= 10"));
                        continue;
                    }

                    gpa = numericGpa;
                }
                else
                {
                    var gpaRaw = gpaCell.GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(gpaRaw))
                    {
                        if (TryParseDoubleFlexible(gpaRaw, out var parsedGpa))
                        {
                            if (parsedGpa < 0)
                            {
                                errors.Add(new ImportUsersError(r, "GPA must be >= 0"));
                                continue;
                            }

                            if (parsedGpa > 10)
                            {
                                errors.Add(new ImportUsersError(r, "GPA must be <= 10"));
                                continue;
                            }

                            gpa = parsedGpa;
                        }
                        else
                        {
                            errors.Add(new ImportUsersError(r, "GPA format is invalid"));
                            continue;
                        }
                    }
                }
            }

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
                string.IsNullOrWhiteSpace(code) ? null : code,
                gpa,
                string.IsNullOrWhiteSpace(semesterCode) ? null : semesterCode
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

        // Deduplicate by student code inside file
        var firstByStudentCode = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filteredByCode = new List<ImportUserRow>(filtered.Count);
        foreach (var row in filtered)
        {
            if (string.IsNullOrWhiteSpace(row.StudentCode))
            {
                filteredByCode.Add(row);
                continue;
            }

            if (firstByStudentCode.Add(row.StudentCode!))
                filteredByCode.Add(row);
            else
                errors.Add(new ImportUsersError(-1, $"Duplicate student code in file: {row.StudentCode} (email: {row.Email})"));
        }

        int created = 0, skipped = 0;

        foreach (var row in filteredByCode)
        {
            // Role map
            var roleId = await roleQueries.GetRoleIdByNameAsync(row.Role, ct);
            if (roleId is null)
            {
                errors.Add(new ImportUsersError(-1, $"Role not found: {row.Role} (email: {row.Email})"));
                skipped++;
                continue;
            }

            var isStudent = string.Equals(row.Role, "student", StringComparison.OrdinalIgnoreCase);
            var existingUserId = await userReadOnlyQueries.GetUserIdByEmailAsync(row.Email, ct);

            Guid? semesterId = null;
            if (isStudent)
            {
                if (string.IsNullOrWhiteSpace(row.SemesterCode))
                {
                    errors.Add(new ImportUsersError(-1, $"SemesterCode is required for student (email: {row.Email})"));
                    skipped++;
                    continue;
                }

                semesterId = ResolveSemesterId(row.SemesterCode, semesters);
                if (!semesterId.HasValue)
                {
                    errors.Add(new ImportUsersError(-1, $"Semester not found: {row.SemesterCode} (email: {row.Email})"));
                    skipped++;
                    continue;
                }
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

            if (!string.IsNullOrWhiteSpace(row.StudentCode))
            {
                var codeOwner = await userReadOnlyQueries.GetByStudentCodeAsync(row.StudentCode!, ct);
                if (codeOwner is not null && (!existingUserId.HasValue || codeOwner.UserId != existingUserId.Value))
                {
                    errors.Add(new ImportUsersError(-1, $"StudentCode already exists: {row.StudentCode} (email: {row.Email})"));
                    skipped++;
                    continue;
                }
            }

            // Exists?
            if (existingUserId.HasValue)
            {
                if (isStudent && semesterId.HasValue)
                {
                    var currentSemesterId = await studentSemesterReadOnly.GetCurrentSemesterIdAsync(existingUserId.Value, ct);
                    if (!currentSemesterId.HasValue || currentSemesterId.Value != semesterId.Value)
                        await studentSemesterWrite.SetCurrentSemesterAsync(existingUserId.Value, semesterId.Value, ct);
                }
                skipped++;
                continue;
            }

            var userId = await userWriteRepo.CreateUserAsync(
                email: row.Email,
                displayName: row.DisplayName,
                studentCode: row.StudentCode,
                gender: row.Gender,
                majorId: majorId,
                gpa: row.Gpa,
                desiredPositionId: null,
                ct: ct);

            await userWriteRepo.AssignRoleAsync(userId, roleId.Value, ct);
            if (isStudent && semesterId.HasValue)
                await studentSemesterWrite.SetCurrentSemesterAsync(userId, semesterId.Value, ct);

            var subject = "TEAMMY - Your account is ready";
            var html = $"""
<p>Hello {System.Net.WebUtility.HtmlEncode(row.DisplayName)},</p>
<p>Your TEAMMY account has been created with this email: <b>{System.Net.WebUtility.HtmlEncode(row.Email)}</b>.</p>
<p>Please login to TEAMMY to complete your profile.</p>
""";
            var sent = await emailSender.SendAsync(row.Email, subject, html, ct);
            if (!sent)
                errors.Add(new ImportUsersError(-1, $"Email failed to send (email: {row.Email})"));

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
        var semesters = await semesterQueries.ListAsync(ct);

        var emailFirstOccurrence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var studentCodeFirstOccurrence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var emailExistsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var emailUserIdCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var studentCodeUserIdCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
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
            string semesterCode = row?.SemesterCode?.Trim() ?? string.Empty;
            var gpa = row?.Gpa;

            bool rowIsEmpty = string.IsNullOrWhiteSpace(email)
                              && string.IsNullOrWhiteSpace(displayName)
                              && string.IsNullOrWhiteSpace(role)
                              && string.IsNullOrWhiteSpace(major)
                              && string.IsNullOrWhiteSpace(gender)
                              && string.IsNullOrWhiteSpace(studentCode)
                              && string.IsNullOrWhiteSpace(semesterCode)
                              && gpa is null;

            if (rowIsEmpty)
            {
                const string emptyMessage = "Row is empty";
                columns.Add(new UserColumnValidation("Email", false, emptyMessage));
                columns.Add(new UserColumnValidation("DisplayName", false, emptyMessage));
                columns.Add(new UserColumnValidation("Role", false, emptyMessage));
                columns.Add(new UserColumnValidation("MajorName", false, emptyMessage));
                columns.Add(new UserColumnValidation("Gender", false, emptyMessage));
                columns.Add(new UserColumnValidation("StudentCode", false, emptyMessage));
                columns.Add(new UserColumnValidation("GPA", false, emptyMessage));
                columns.Add(new UserColumnValidation("SemesterCode", false, emptyMessage));
                messages.Add("Row has no data");
                invalidCount++;
                results.Add(new UserImportRowValidation(rowNumber, false, columns, messages));
                continue;
            }

            bool emailValid = true;
            var emailErrors = new List<string>();
            var emailExists = false;

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
                // Existing email is allowed; row will be treated as update/skip.
                emailValid = true;
                emailExists = true;
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
            bool studentCodeValid = true;
            string? studentCodeError = null;
            if (!string.IsNullOrWhiteSpace(studentCode))
            {
                if (studentCode.Length > 30)
                {
                    studentCodeValid = false;
                    studentCodeError = "StudentCode must be <= 30 characters";
                }
                else if (!studentCodeFirstOccurrence.TryAdd(studentCode, rowNumber))
                {
                    studentCodeValid = false;
                    studentCodeError = "Duplicate StudentCode within payload";
                }
                else
                {
                    var codeOwner = await GetUserIdByStudentCodeAsync(studentCode, studentCodeUserIdCache, ct);
                    if (codeOwner.HasValue)
                    {
                        var userId = await GetUserIdByEmailAsync(email, emailUserIdCache, ct);
                        if (!userId.HasValue || userId.Value != codeOwner.Value)
                        {
                            studentCodeValid = false;
                            studentCodeError = "StudentCode already exists in system";
                        }
                    }
                }
            }
            columns.Add(new UserColumnValidation("StudentCode", studentCodeValid, studentCodeError));

            bool gpaValid = true;
            string? gpaError = null;
            if (gpa is not null)
            {
                if (gpa < 0)
                {
                    gpaValid = false;
                    gpaError = "GPA must be >= 0";
                }
                else if (gpa > 10)
                {
                    gpaValid = false;
                    gpaError = "GPA must be <= 10";
                }
            }
            columns.Add(new UserColumnValidation("GPA", gpaValid, gpaError));

            bool semesterValid = true;
            string? semesterError = null;
            if (string.Equals(role, "student", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(semesterCode))
                {
                    semesterValid = false;
                    semesterError = "SemesterCode is required for student";
                }
                else
                {
                    var semId = ResolveSemesterId(semesterCode, semesters);
                    if (!semId.HasValue)
                    {
                        semesterValid = false;
                        semesterError = $"Semester '{semesterCode}' not found";
                    }
                    else if (emailExists)
                    {
                        var userId = await GetUserIdByEmailAsync(email, emailUserIdCache, ct);
                        if (userId.HasValue)
                        {
                            var current = await studentSemesterReadOnly.GetCurrentSemesterIdAsync(userId.Value, ct);
                            if (current.HasValue && current.Value == semId.Value)
                                messages.Add("Existing student in same semester; row will be skipped.");
                            else
                                messages.Add("Existing student; semester will be updated.");
                        }
                    }
                }
            }
            else if (emailExists)
            {
                messages.Add("Existing user; row will be skipped.");
            }
            columns.Add(new UserColumnValidation("SemesterCode", semesterValid, semesterError));

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

        async Task<Guid?> GetUserIdByEmailAsync(string normalizedEmail, Dictionary<string, Guid?> cache, CancellationToken token)
        {
            if (cache.TryGetValue(normalizedEmail, out var id)) return id;
            id = await userReadOnlyQueries.GetUserIdByEmailAsync(normalizedEmail, token);
            cache[normalizedEmail] = id;
            return id;
        }

        async Task<Guid?> GetUserIdByStudentCodeAsync(string studentCode, Dictionary<string, Guid?> cache, CancellationToken token)
        {
            if (cache.TryGetValue(studentCode, out var id)) return id;
            var detail = await userReadOnlyQueries.GetByStudentCodeAsync(studentCode, token);
            id = detail?.UserId;
            cache[studentCode] = id;
            return id;
        }
    }

    private static bool IsValidEmail(string email)
    {
        try { _ = new MailAddress(email); return true; }
        catch { return false; }
    }

    private static bool TryParseDoubleFlexible(string raw, out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Normalize common user inputs:
        // - "3,5" => "3.5" (decimal comma)
        // - "3.5" stays as-is
        // - If both separators exist, treat the last one as the decimal separator
        //   and strip the other as thousands separator.
        var s = raw.Trim().Replace(" ", string.Empty);

        var hasDot = s.Contains('.');
        var hasComma = s.Contains(',');

        if (hasDot && hasComma)
        {
            var lastDot = s.LastIndexOf('.');
            var lastComma = s.LastIndexOf(',');
            if (lastDot > lastComma)
            {
                // dot is decimal separator -> remove commas
                s = s.Replace(",", string.Empty);
            }
            else
            {
                // comma is decimal separator -> remove dots, convert comma to dot
                s = s.Replace(".", string.Empty).Replace(',', '.');
            }
        }
        else if (hasComma && !hasDot)
        {
            s = s.Replace(',', '.');
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string BuildSemesterCode(string? season, int year)
    {
        var s = (season ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(s) || year <= 0 ? string.Empty : $"{s}{year}";
    }

    private static Guid? ResolveSemesterId(string semesterCode, IReadOnlyList<SemesterSummaryDto> semesters)
    {
        if (string.IsNullOrWhiteSpace(semesterCode))
            return null;

        var normalized = semesterCode.Trim().Replace(" ", string.Empty);
        var (seasonKey, year) = ParseSemesterCode(normalized);
        if (string.IsNullOrWhiteSpace(seasonKey))
            return null;

        var matches = semesters
            .Where(s => !string.IsNullOrWhiteSpace(s.Season) && s.Year > 0)
            .Select(s => new
            {
                s.SemesterId,
                Season = NormalizeSeasonToken(s.Season!),
                s.Year
            })
            .Where(s => s.Season == seasonKey)
            .ToList();

        if (matches.Count == 0)
            return null;

        if (year.HasValue)
            return matches.FirstOrDefault(x => x.Year == year.Value)?.SemesterId;

        return matches.OrderByDescending(x => x.Year).FirstOrDefault()?.SemesterId;
    }

    private static (string? SeasonKey, int? Year) ParseSemesterCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var letters = new string(raw.Where(char.IsLetter).ToArray());
        var digits = new string(raw.Where(char.IsDigit).ToArray());

        var season = NormalizeSeasonToken(letters);
        if (string.IsNullOrWhiteSpace(season))
            return (null, null);

        if (string.IsNullOrWhiteSpace(digits))
            return (season, null);

        if (!int.TryParse(digits, out var yearRaw))
            return (season, null);

        var year = yearRaw < 100 ? 2000 + yearRaw : yearRaw;
        return (season, year);
    }

    private static string? NormalizeSeasonToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim().ToUpperInvariant();
        return s switch
        {
            "FALL" or "FA" or "F" => "FALL",
            "SUMMER" or "SU" or "S" => "SUMMER",
            "SPRING" or "SP" => "SPRING",
            _ => s
        };
    }
}
