using Teammy.Application.Reports.Dtos;

namespace Teammy.Application.Reports;

public interface IReportExportService
{
    Task<ReportFileResult> ExportAsync(ReportRequest request, CancellationToken ct);
}
