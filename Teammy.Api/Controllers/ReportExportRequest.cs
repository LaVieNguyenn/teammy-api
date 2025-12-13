namespace Teammy.Api.Controllers;

public sealed class ReportExportRequest
{
    public Guid? SemesterId { get; set; }
    public Guid? MajorId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
