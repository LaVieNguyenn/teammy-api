using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teammy.Application.Ai.Dtos;
using Teammy.Application.Ai.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiMatchingController : ControllerBase
{
    private readonly AiMatchingService _service;
    private readonly IGroupReadOnlyQueries _groupQueries;

    public AiMatchingController(AiMatchingService service, IGroupReadOnlyQueries groupQueries)
    {
        _service = service;
        _groupQueries = groupQueries;
    }

    [HttpGet("summary")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AiResponse<AiSummaryDto>>> GetSummary([FromQuery] Guid? semesterId, CancellationToken ct)
    {
        return await HandleAiRequestAsync(() => _service.GetSummaryAsync(semesterId, ct));
    }

    [HttpGet("options")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AiResponse<AiOptionListDto>>> GetOptions([FromQuery] AiOptionRequest? request, CancellationToken ct)
    {
        return await HandleAiRequestAsync(() => _service.GetOptionsAsync(request, ct));
    }

    [HttpPost("recruitment-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<AiResponse<IReadOnlyList<object>>>> SuggestRecruitmentPosts(
        [FromBody] RecruitmentPostSuggestionRequest request,
        CancellationToken ct)
    {
        return await HandleAiRequestAsync(async () =>
        {
            var suggestions = await _service.SuggestRecruitmentPostsForStudentAsync(GetCurrentUserId(), request, ct);
            return await ShapeRecruitmentPostSuggestionsAsync(suggestions, ct);
        });
    }

    [HttpPost("topic-suggestions")]
    [Authorize]
    public async Task<ActionResult<AiResponse<IReadOnlyList<TopicSuggestionDto>>>> SuggestTopics(
        [FromBody] TopicSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return await HandleAiRequestAsync(() =>
            _service.SuggestTopicsForGroupAsync(GetCurrentUserId(), request, ct));
    }

    [HttpPost("profile-post-suggestions")]
    [Authorize]
    public async Task<ActionResult<AiResponse<IReadOnlyList<object>>>> SuggestProfilePosts(
        [FromBody] ProfilePostSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return await HandleAiRequestAsync(async () =>
        {
            var suggestions = await _service.SuggestProfilePostsForGroupAsync(GetCurrentUserId(), request, ct);
            return await ShapeProfilePostSuggestionsAsync(suggestions, ct);
        });
    }

    [HttpPost("auto-assign/teams")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AiResponse<AutoAssignTeamsResultDto>>> AutoAssignTeams(
        [FromBody] AutoAssignTeamsRequest? request,
        CancellationToken ct)
    {
        return await HandleAiRequestAsync(() => _service.AutoAssignTeamsAsync(GetCurrentUserId(), request, ct));
    }

    [HttpPost("auto-assign/topic")]
    [Authorize]
    public async Task<ActionResult<AiResponse<AutoAssignTopicBatchResultDto>>> AutoAssignTopic(
        [FromBody] AutoAssignTopicRequest request,
        CancellationToken ct)
    {
        var isAdmin = User.IsInRole("admin") || User.IsInRole("moderator");
        if (!isAdmin && (request is null || !request.GroupId.HasValue))
            return StatusCode(StatusCodes.Status403Forbidden,
                AiResponse<AutoAssignTopicBatchResultDto>.FromError("Chỉ admin/moderator mới được phép auto assign cho toàn bộ nhóm."));

        return await HandleAiRequestAsync(() =>
            _service.AutoAssignTopicAsync(GetCurrentUserId(), isAdmin, request, ct));
    }

    [HttpPost("auto-resolve")]
    [Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<AiResponse<AiAutoResolveResultDto>>> AutoResolve(
        [FromBody] AiAutoResolveRequest request,
        CancellationToken ct)
    {
        return await HandleAiRequestAsync(() =>
            _service.AutoResolveAsync(GetCurrentUserId(), request, ct));
    }

    private async Task<IReadOnlyList<object>> ShapeRecruitmentPostSuggestionsAsync(
        IReadOnlyList<RecruitmentPostSuggestionDto> suggestions,
        CancellationToken ct)
    {
        if (suggestions.Count == 0)
            return Array.Empty<object>();

        var shaped = new List<object>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            var postData = suggestion.Detail is not null
                ? await BuildRecruitmentPostShapeAsync(suggestion.Detail, ct)
                : BuildFallbackRecruitmentPostShape(suggestion);

            shaped.Add(new
            {
                scorePercent = suggestion.Score,
                matchingSkills = suggestion.MatchingSkills,
                requiredSkills = suggestion.RequiredSkills,
                post = postData
            });
        }

        return shaped;
    }

    private async Task<object> BuildRecruitmentPostShapeAsync(RecruitmentPostDetailDto detail, CancellationToken ct)
    {
        IReadOnlyList<GroupMemberDto>? membersDetail = null;
        GroupMemberDto? leaderDetail = null;
        if (detail.GroupId is Guid gid)
        {
            var members = await _groupQueries.ListActiveMembersAsync(gid, ct);
            leaderDetail = members.FirstOrDefault(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase));
            membersDetail = members.Where(m => !string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var topLevelMajor = detail.Major ?? detail.Group?.Major;
        var topicObj = detail.Group?.Topic;

        return new
        {
            id = detail.Id,
            type = detail.Type,
            status = detail.Status,
            title = detail.Title,
            description = detail.Description,
            position_needed = detail.PositionNeeded,
            skills = detail.Skills,
            createdAt = detail.CreatedAt,
            applicationDeadline = detail.ApplicationDeadline,
            currentMembers = detail.CurrentMembers,
            applicationsCount = detail.ApplicationsCount,
            hasApplied = detail.HasApplied,
            myApplicationId = detail.MyApplicationId,
            myApplicationStatus = detail.MyApplicationStatus,
            semester = detail.Semester,
            mentor = detail.Group?.Mentor,
            group = detail.Group is null
                ? null
                : new
                {
                    detail.Group.GroupId,
                    detail.Group.SemesterId,
                    detail.Group.MentorId,
                    detail.Group.Name,
                    detail.Group.Description,
                    detail.Group.Status,
                    detail.Group.MaxMembers,
                    detail.Group.MajorId,
                    detail.Group.TopicId,
                    detail.Group.CreatedAt,
                    detail.Group.UpdatedAt,
                    leader = leaderDetail,
                    members = membersDetail,
                    mentor = detail.Group.Mentor
                },
            major = topLevelMajor,
            topic = topicObj,
            topicName = topicObj?.Title
        };
    }

    private static object BuildFallbackRecruitmentPostShape(RecruitmentPostSuggestionDto suggestion)
        => new
        {
            id = suggestion.PostId,
            type = "group_hiring",
            status = "open",
            title = suggestion.Title,
            description = suggestion.Description,
            position_needed = suggestion.PositionNeeded,
            skills = suggestion.RequiredSkills,
            createdAt = suggestion.CreatedAt,
            applicationDeadline = suggestion.ApplicationDeadline,
            major = new { majorId = suggestion.MajorId, majorName = suggestion.MajorName },
            group = suggestion.GroupId is null ? null : new { groupId = suggestion.GroupId, groupName = suggestion.GroupName }
        };

    private async Task<IReadOnlyList<object>> ShapeProfilePostSuggestionsAsync(
        IReadOnlyList<ProfilePostSuggestionDto> suggestions,
        CancellationToken ct)
    {
        if (suggestions.Count == 0)
            return Array.Empty<object>();

        var shaped = new List<object>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            var profileData = suggestion.Detail is not null
                ? await BuildProfilePostShapeAsync(suggestion.Detail, ct)
                : BuildFallbackProfilePostShape(suggestion);

            shaped.Add(new
            {
                scorePercent = suggestion.Score,
                primaryRole = suggestion.PrimaryRole,
                matchingSkills = suggestion.MatchingSkills,
                profilePost = profileData
            });
        }

        return shaped;
    }

    private async Task<object> BuildProfilePostShapeAsync(ProfilePostDetailDto detail, CancellationToken ct)
    {
        Guid[]? memberUserIds = null;
        Guid? userGroupId = null;
        if (detail.User?.UserId is Guid uid)
        {
            var check = await _groupQueries.CheckUserGroupAsync(uid, detail.SemesterId, includePending: false, ct);
            if (check.HasGroup && check.GroupId.HasValue)
            {
                userGroupId = check.GroupId.Value;
                var members = await _groupQueries.ListActiveMembersAsync(userGroupId.Value, ct);
                memberUserIds = members.Select(m => m.UserId).ToArray();
            }
        }

        return new
        {
            id = detail.Id,
            type = detail.Type,
            status = detail.Status,
            title = detail.Title,
            description = detail.Description,
            position_needed = detail.Skills,
            createdAt = detail.CreatedAt,
            hasApplied = detail.HasApplied,
            myApplicationId = detail.MyApplicationId,
            myApplicationStatus = detail.MyApplicationStatus,
            semester = detail.Semester,
            user = detail.User,
            major = detail.Major,
            userGroup = userGroupId is null ? null : new { groupId = userGroupId.Value, memberUserIds }
        };
    }

    private static object BuildFallbackProfilePostShape(ProfilePostSuggestionDto suggestion)
        => new
        {
            id = suggestion.PostId,
            type = "individual",
            status = "open",
            title = suggestion.Title,
            description = suggestion.Description,
            position_needed = suggestion.SkillsText,
            createdAt = suggestion.CreatedAt,
            hasApplied = false,
            myApplicationId = (Guid?)null,
            myApplicationStatus = (string?)null,
            user = new { userId = suggestion.OwnerUserId, displayName = suggestion.OwnerDisplayName }
        };

    private async Task<ActionResult<AiResponse<T>>> HandleAiRequestAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var data = await action();
            return Ok(AiResponse<T>.FromSuccess(data));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, AiResponse<T>.FromError(ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return Ok(AiResponse<T>.FromError(ex.Message));
        }
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("user_id");
        if (!Guid.TryParse(sub, out var id))
            throw new UnauthorizedAccessException("Invalid token");
        return id;
    }
}
