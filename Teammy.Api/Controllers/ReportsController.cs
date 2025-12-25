using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Reports;
using Teammy.Application.Reports.Dtos;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(
    IReportExportService exportService,
    ISemesterReadOnlyQueries semesterQueries,
    IMajorReadOnlyQueries majorQueries,
    IGroupReadOnlyQueries groupQueries,
    ITopicReadOnlyQueries topicQueries) : ControllerBase
{
    private readonly IReportExportService _exportService = exportService;
    private readonly ISemesterReadOnlyQueries _semesterQueries = semesterQueries;
    private readonly IMajorReadOnlyQueries _majorQueries = majorQueries;
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;
    private readonly ITopicReadOnlyQueries _topicQueries = topicQueries;

    [HttpGet("options")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<ReportSummaryResponse>> GetSummary([FromQuery] Guid? semesterId, [FromQuery] Guid? majorId, CancellationToken ct)
    {
        var groups = await _groupQueries.ListGroupsAsync(
            status: null,
            majorId: majorId,
            topicId: null,
            ct);

        var filtered = groups
            .Where(g => !semesterId.HasValue || g.Semester.SemesterId == semesterId.Value)
            .ToList();

        var semesterLabel = semesterId.HasValue
            ? (await _semesterQueries.GetByIdAsync(semesterId.Value, ct)) is { } semester
                ? $"{semester.Season} {semester.Year}"
                : null
            : "All semesters";

        var majorLabel = majorId.HasValue
            ? (await _majorQueries.GetAsync(majorId.Value, ct))?.MajorName
            : "All majors";

        var topics = await _topicQueries.GetAllAsync(
            q: null,
            semesterId,
            status: null,
            majorId,
            ownerUserId: null,
            ct);

        var metrics = BuildSummaryMetrics(filtered, topics.Count);

        var response = new ReportSummaryResponse
        {
            Filter = new ReportSummaryFilterDto(semesterLabel, majorLabel),
            Metrics = metrics
        };

        return Ok(response);
    }

    [HttpPost("export")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<IActionResult> Export([FromBody] ReportExportRequest request, CancellationToken ct)
    {
        var internalRequest = MapRequest(request);
        var result = await _exportService.ExportAsync(internalRequest, ct);
        return File(
            result.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.FileName);
    }

    private static ReportRequest MapRequest(ReportExportRequest request)
    {
        DateTime? startUtc = null;
        DateTime? endUtc = null;

        if (request.StartDate.HasValue)
        {
            startUtc = request.StartDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        if (request.EndDate.HasValue)
        {
            endUtc = request.EndDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        return new ReportRequest
        {
            SemesterId = request.SemesterId,
            MajorId = request.MajorId,
            IncludeGroups = true,
            IncludeGroupMembers = true,
            IncludeRecruitmentPosts = true,
            IncludeMilestones = true,
            IncludeTasks = true,
            IncludeActivityLogs = true,
            ActivityLogLimit = 200,
            StartDateUtc = startUtc,
            EndDateUtc = endUtc
        };
    }

    private static IReadOnlyList<ReportSummaryMetricDto> BuildSummaryMetrics(IReadOnlyList<GroupSummaryDto> groups, int topicCount)
    {
        var groupCount = groups.Count;
        var memberCount = groups.Sum(g => g.CurrentMembers);

        return new[]
        {
            new ReportSummaryMetricDto(
                "Groups",
                groupCount,
                "Total number of study groups formed"),
            new ReportSummaryMetricDto(
                "Members",
                memberCount,
                "Total active members across all groups"),
            new ReportSummaryMetricDto(
                "Topics",
                topicCount,
                "Number of unique study topics")
        };
    }
}
