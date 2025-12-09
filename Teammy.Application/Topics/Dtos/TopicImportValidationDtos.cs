namespace Teammy.Application.Topics.Dtos;

public sealed record TopicImportValidationRequest(
    IReadOnlyList<TopicImportPayloadRow> Rows
);

public sealed record TopicImportPayloadRow(
    int RowNumber,
    string? SemesterCode,
    string? Title,
    string? Description,
    string? Status,
    string? MajorName,
    IReadOnlyList<string>? MentorEmails
);

public sealed record TopicImportValidationResult(
    TopicValidationSummary Summary,
    IReadOnlyList<TopicImportRowValidation> Rows
);

public sealed record TopicValidationSummary(int TotalRows, int ValidRows, int InvalidRows);

public sealed record TopicImportRowValidation(
    int RowNumber,
    bool IsValid,
    IReadOnlyList<TopicColumnValidation> Columns,
    IReadOnlyList<string> Messages
);

public sealed record TopicColumnValidation(string Column, bool IsValid, string? ErrorMessage);

public static class TopicImportColumns
{
    public const string SemesterCode = "SemesterCode";
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Status = "Status";
    public const string MajorName = "MajorName";
    public const string MentorEmails = "MentorEmails";

    public static readonly string[] All =
    {
        SemesterCode,
        Title,
        Description,
        Status,
        MajorName,
        MentorEmails
    };
}
