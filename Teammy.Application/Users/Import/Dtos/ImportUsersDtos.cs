namespace Teammy.Application.Users.Import.Dtos;

public sealed record ImportUsersResult(
    int TotalRows,
    int CreatedCount,
    int SkippedCount,
    IReadOnlyList<ImportUsersError> Errors
);

public sealed record ImportUsersError(int RowNumber, string Message);

public sealed record ImportUserRow(
    string Email,
    string DisplayName,
    string Role,       
    string? MajorName,  
    string? Gender,    
    string? StudentCode,
    double? Gpa
);

public sealed record UserImportValidationRequest(
    IReadOnlyList<UserImportPayloadRow> Rows
);

public sealed record UserImportPayloadRow(
    int RowNumber,
    string? Email,
    string? DisplayName,
    string? Role,
    string? MajorName,
    string? Gender,
    string? StudentCode,
    double? Gpa
);

public sealed record UserImportValidationResult(
    UsersValidationSummary Summary,
    IReadOnlyList<UserImportRowValidation> Rows
);

public sealed record UsersValidationSummary(int TotalRows, int ValidRows, int InvalidRows);

public sealed record UserImportRowValidation(
    int RowNumber,
    bool IsValid,
    IReadOnlyList<UserColumnValidation> Columns,
    IReadOnlyList<string> Messages
);

public sealed record UserColumnValidation(string Column, bool IsValid, string? ErrorMessage);
