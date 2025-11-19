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
