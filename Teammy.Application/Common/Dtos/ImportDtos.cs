using System.Collections.Generic;

namespace Teammy.Application.Common.Dtos;

public sealed record ImportErrorDto(int Row, string Message);

public sealed record ImportResultDto(
    int Total,
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportErrorDto> Errors,
    IReadOnlyList<ImportErrorDto> SkippedReasons
);
