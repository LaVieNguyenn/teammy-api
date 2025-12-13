namespace Teammy.Application.Reports.Dtos;

public sealed class ReportSummaryResponse
{
    public ReportSummaryFilterDto Filter { get; init; } = new();
    public IReadOnlyList<ReportSummaryMetricDto> Metrics { get; init; } = Array.Empty<ReportSummaryMetricDto>();
}

public sealed record ReportSummaryFilterDto(string? Semester = null, string? Major = null);

public sealed record ReportSummaryMetricDto(string Metric, int Count, string Description);
