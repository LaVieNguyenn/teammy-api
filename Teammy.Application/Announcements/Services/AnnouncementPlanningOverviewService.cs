using System.Linq;
using Teammy.Application.Ai.Models;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Announcements.Dtos;

namespace Teammy.Application.Announcements.Services;

public sealed class AnnouncementPlanningOverviewService(
    IAiMatchingQueries aiQueries,
    IGroupReadOnlyQueries groupQueries,
    IMajorReadOnlyQueries majorQueries)
{
    public async Task<AnnouncementPlanningOverviewDto> GetOverviewAsync(
        AnnouncementPlanningOverviewRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var semesterId = await groupQueries.GetActiveSemesterIdAsync(ct)
            ?? throw new InvalidOperationException("No active semester");

        var semesterInfo = await groupQueries.GetSemesterAsync(semesterId, ct)
            ?? throw new InvalidOperationException("Active semester not found");

        var label = BuildSemesterLabel(semesterInfo.Season, semesterInfo.Year)
            ?? semesterId.ToString();

        var majorList = await majorQueries.ListAsync(ct);
        var majorName = majorList.FirstOrDefault(m => m.MajorId == request.MajorId).MajorName;
        if (string.IsNullOrWhiteSpace(majorName))
            majorName = null;

        var groupsWithoutTopicAll = await aiQueries.ListGroupsWithoutTopicAsync(semesterId, ct);
        var groupsWithoutTopic = groupsWithoutTopicAll
            .Where(g => !string.Equals(g.Status, "closed", StringComparison.OrdinalIgnoreCase))
            .Where(g => g.MajorId.HasValue && g.MajorId.Value == request.MajorId)
            .Select(ToGroupItem)
            .ToList();

        // Groups needing members = groups with remaining slots in this semester.
        var allMajorGroups = await groupQueries.ListGroupsAsync(status: null, majorId: request.MajorId, topicId: null, ct);
        var groupsWithoutMember = allMajorGroups
            .Where(g => g.Semester.SemesterId == semesterId)
            .Where(g => !string.Equals(g.Status, "closed", StringComparison.OrdinalIgnoreCase))
            .Where(g => g.CurrentMembers < g.MaxMembers)
            .Select(g => new PlanningGroupItemDto(
                g.Id,
                g.Name,
                g.Description,
                g.Topic?.TopicId,
                g.Mentor?.Id,
                g.MaxMembers,
                g.CurrentMembers,
                g.Status))
            .ToList();

        var unassigned = await aiQueries.ListUnassignedStudentsAsync(semesterId, request.MajorId, ct);
        var studentsWithoutGroup = unassigned
            .Select(s => new PlanningStudentItemDto(
                s.UserId,
                s.DisplayName,
                s.MajorId,
                s.PrimaryRole,
                ExtractTopSkillTags(s.SkillsJson, max: 5)))
            .ToList();

        return new AnnouncementPlanningOverviewDto(
            semesterId,
            label,
            request.MajorId,
            majorName,
            groupsWithoutTopic.Count,
            groupsWithoutMember.Count,
            studentsWithoutGroup.Count,
            groupsWithoutTopic,
            groupsWithoutMember,
            studentsWithoutGroup);
    }

    private static PlanningGroupItemDto ToGroupItem(GroupOverviewSnapshot g)
        => new(
            g.GroupId,
            g.Name,
            g.Description,
            g.TopicId,
            g.MentorId,
            g.MaxMembers,
            g.CurrentMembers,
            g.Status);

    private static IReadOnlyList<string> ExtractTopSkillTags(string? skillsJson, int max)
    {
        if (string.IsNullOrWhiteSpace(skillsJson) || max <= 0)
            return Array.Empty<string>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(skillsJson);
            if (doc.RootElement.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var tags = new List<string>();
                foreach (var t in tagsEl.EnumerateArray())
                {
                    if (t.ValueKind != System.Text.Json.JsonValueKind.String)
                        continue;
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        tags.Add(s!);
                    if (tags.Count >= max)
                        break;
                }
                return tags;
            }
        }
        catch
        {
            // ignore malformed JSON
        }

        return Array.Empty<string>();
    }

    private static string? BuildSemesterLabel(string? season, int? year)
    {
        if (string.IsNullOrWhiteSpace(season) && !year.HasValue)
            return null;

        return year.HasValue
            ? $"{season ?? string.Empty} {year.Value}".Trim()
            : season;
    }
}
