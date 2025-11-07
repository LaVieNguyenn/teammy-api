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
    string? StudentCode 
);
