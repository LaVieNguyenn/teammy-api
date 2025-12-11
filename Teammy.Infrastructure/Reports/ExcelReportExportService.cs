using System.Globalization;
using ClosedXML.Excel;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.ProjectTracking.Interfaces;
using Teammy.Application.Reports;
using Teammy.Application.Reports.Dtos;

namespace Teammy.Infrastructure.Reports;

public sealed class ExcelReportExportService(
    IGroupReadOnlyQueries groupQueries,
    IRecruitmentPostReadOnlyQueries postQueries,
    IActivityLogRepository activityLogRepository,
    IProjectTrackingReadOnlyQueries projectTrackingQueries) : IReportExportService
{
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;
    private readonly IRecruitmentPostReadOnlyQueries _postQueries = postQueries;
    private readonly IActivityLogRepository _activityLogRepository = activityLogRepository;
    private readonly IProjectTrackingReadOnlyQueries _projectTrackingQueries = projectTrackingQueries;

    public async Task<ReportFileResult> ExportAsync(ReportRequest? request, CancellationToken ct)
    {
        request ??= new ReportRequest();

        using var workbook = new XLWorkbook();

        IReadOnlyList<GroupSummaryDto> groupSummaries = Array.Empty<GroupSummaryDto>();
        var shouldLoadGroups = request.IncludeGroups || request.IncludeGroupMembers || request.IncludeMilestones;
        if (shouldLoadGroups)
        {
            var groups = await _groupQueries.ListGroupsAsync(
                request.GroupStatus,
                request.MajorId,
                null,
                ct);

            groupSummaries = groups
                .Where(g =>
                    (!request.SemesterId.HasValue || g.Semester.SemesterId == request.SemesterId.Value) &&
                    (!request.GroupId.HasValue || g.Id == request.GroupId.Value))
                .ToList();
        }

        if (request.IncludeGroups)
        {
            WriteGroupSheet(workbook, groupSummaries);
        }

        if (request.IncludeGroupMembers)
        {
            await WriteGroupMembersSheetAsync(workbook, groupSummaries, ct);
        }

        if (request.IncludeMilestones)
        {
            await WriteMilestonesSheetAsync(workbook, groupSummaries, ct);
        }

        if (request.IncludeRecruitmentPosts)
        {
            var posts = await _postQueries.ListAsync(
                skills: null,
                majorId: request.MajorId,
                status: request.RecruitmentStatus,
                expand: ExpandOptions.Semester | ExpandOptions.Group | ExpandOptions.Major,
                currentUserId: null,
                ct);

            var filteredPosts = posts
                .Where(p =>
                    (!request.GroupId.HasValue || p.GroupId == request.GroupId.Value) &&
                    (!request.SemesterId.HasValue ||
                     (p.Group?.SemesterId == request.SemesterId.Value ||
                      p.Semester?.SemesterId == request.SemesterId.Value ||
                      p.SemesterId == request.SemesterId.Value)))
                .ToList();

            WriteRecruitmentSheet(workbook, filteredPosts);
        }

        if (request.IncludeActivityLogs)
        {
            var logRequest = new ActivityLogListRequest
            {
                GroupId = request.GroupId,
                Limit = Math.Min(request.ActivityLogLimit, 200)
            };
            var logs = await _activityLogRepository.ListAsync(logRequest, ct);
            WriteActivityLogSheet(workbook, logs);
        }

        if (workbook.Worksheets.Count == 0)
        {
            var ws = workbook.Worksheets.Add("Report");
            ws.Cell(1, 1).Value = "No data for the selected filters.";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"teammy-report-{DateTime.UtcNow:yyyyMMddHHmm}.xlsx";
        return new ReportFileResult(fileName, stream.ToArray());
    }

    private static void WriteGroupSheet(XLWorkbook workbook, IReadOnlyList<GroupSummaryDto> groups)
    {
        var ws = workbook.Worksheets.Add("Groups");
        var headers = new[]
        {
            "Group Id", "Group Name", "Semester", "Status", "Max Members",
            "Current Members", "Major", "Topic", "Mentor", "Skills"
        };

        WriteHeader(ws, headers);

        var row = 2;
        foreach (var g in groups)
        {
            ws.Cell(row, 1).Value = g.Id.ToString();
            ws.Cell(row, 2).Value = g.Name;
            ws.Cell(row, 3).Value = $"{g.Semester.Season} {g.Semester.Year}";
            ws.Cell(row, 4).Value = g.Status;
            ws.Cell(row, 5).Value = g.MaxMembers;
            ws.Cell(row, 6).Value = g.CurrentMembers;
            ws.Cell(row, 7).Value = g.Major?.MajorName ?? "-";
            ws.Cell(row, 8).Value = g.Topic?.Title ?? "-";
            ws.Cell(row, 9).Value = g.Mentor?.DisplayName ?? "-";
            ws.Cell(row, 10).Value = g.Skills is null ? string.Empty : string.Join(", ", g.Skills);
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private async Task WriteGroupMembersSheetAsync(
        XLWorkbook workbook,
        IReadOnlyList<GroupSummaryDto> groups,
        CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Members");
        var headers = new[]
        {
            "Group Id", "Group Name", "Member Email", "Member Name",
            "Role", "Joined At"
        };
        WriteHeader(ws, headers);

        var row = 2;
        foreach (var group in groups)
        {
            var members = await _groupQueries.ListActiveMembersAsync(group.Id, ct);
            foreach (var member in members)
            {
                ws.Cell(row, 1).Value = group.Id.ToString();
                ws.Cell(row, 2).Value = group.Name;
                ws.Cell(row, 3).Value = member.Email;
                ws.Cell(row, 4).Value = member.DisplayName;
                ws.Cell(row, 5).Value = member.AssignedRole ?? member.Role;
                ws.Cell(row, 6).Value = member.JoinedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                row++;
            }
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteRecruitmentSheet(XLWorkbook workbook, IReadOnlyList<RecruitmentPostSummaryDto> posts)
    {
        var ws = workbook.Worksheets.Add("Recruitment Posts");
        var headers = new[]
        {
            "Post Id", "Title", "Type", "Status", "Group",
            "Semester", "Major", "Position Needed", "Skills",
            "Applications Count", "Created At", "Deadline"
        };
        WriteHeader(ws, headers);

        var row = 2;
        foreach (var post in posts)
        {
            ws.Cell(row, 1).Value = post.Id.ToString();
            ws.Cell(row, 2).Value = post.Title;
            ws.Cell(row, 3).Value = post.Type;
            ws.Cell(row, 4).Value = post.Status;
            ws.Cell(row, 5).Value = post.GroupName ?? "-";
            ws.Cell(row, 6).Value = post.SemesterName ?? $"{post.Semester?.Season} {post.Semester?.Year}";
            ws.Cell(row, 7).Value = post.MajorName ?? post.Major?.MajorName ?? "-";
            ws.Cell(row, 8).Value = post.PositionNeeded ?? "-";
            ws.Cell(row, 9).Value = post.Skills is null ? string.Empty : string.Join(", ", post.Skills);
            ws.Cell(row, 10).Value = post.ApplicationsCount;
            ws.Cell(row, 11).Value = post.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            ws.Cell(row, 12).Value = post.ApplicationDeadline?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteActivityLogSheet(XLWorkbook workbook, IReadOnlyList<ActivityLogDto> logs)
    {
        var ws = workbook.Worksheets.Add("Activity Logs");
        var headers = new[]
        {
            "Timestamp", "Action", "Entity Type", "Entity Id",
            "Actor", "Actor Email", "Target User", "Message",
            "Status", "Severity", "Platform"
        };
        WriteHeader(ws, headers);

        var row = 2;
        foreach (var log in logs)
        {
            ws.Cell(row, 1).Value = log.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            ws.Cell(row, 2).Value = log.Action;
            ws.Cell(row, 3).Value = log.EntityType;
            ws.Cell(row, 4).Value = log.EntityId?.ToString() ?? "-";
            ws.Cell(row, 5).Value = log.ActorDisplayName ?? log.ActorId.ToString();
            ws.Cell(row, 6).Value = log.ActorEmail ?? "-";
            ws.Cell(row, 7).Value = log.TargetDisplayName ?? log.TargetUserId?.ToString() ?? "-";
            ws.Cell(row, 8).Value = log.Message ?? "-";
            ws.Cell(row, 9).Value = log.Status;
            ws.Cell(row, 10).Value = log.Severity;
            ws.Cell(row, 11).Value = log.Platform ?? "-";
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private async Task WriteMilestonesSheetAsync(
        XLWorkbook workbook,
        IReadOnlyList<GroupSummaryDto> groups,
        CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Milestones");
        var headers = new[]
        {
            "Group Id", "Group Name", "Milestone Id", "Milestone Name",
            "Status", "Target Date", "Total Items", "Completed Items",
            "Completion %", "Created At", "Updated At", "Description"
        };
        WriteHeader(ws, headers);

        var row = 2;
        foreach (var group in groups)
        {
            var milestones = await _projectTrackingQueries.ListMilestonesAsync(group.Id, ct);
            foreach (var milestone in milestones)
            {
                ws.Cell(row, 1).Value = group.Id.ToString();
                ws.Cell(row, 2).Value = group.Name;
                ws.Cell(row, 3).Value = milestone.MilestoneId.ToString();
                ws.Cell(row, 4).Value = milestone.Name;
                ws.Cell(row, 5).Value = milestone.Status;
                ws.Cell(row, 6).Value = milestone.TargetDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                ws.Cell(row, 7).Value = milestone.TotalItems;
                ws.Cell(row, 8).Value = milestone.CompletedItems;
                ws.Cell(row, 9).Value = $"{milestone.CompletionPercent:P1}";
                ws.Cell(row, 10).Value = milestone.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                ws.Cell(row, 11).Value = milestone.UpdatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                ws.Cell(row, 12).Value = milestone.Description ?? "-";
                row++;
            }
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteHeader(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Count);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
    }
}
