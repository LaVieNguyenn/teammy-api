using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Teammy.Application.Ai.Dtos;
using Teammy.Application.Ai.Models;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Semesters.Dtos;

namespace Teammy.Application.Ai.Services;

public sealed class AiMatchingService(
    IAiMatchingQueries aiQueries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepository,
    ISemesterReadOnlyQueries semesterQueries,
    IRecruitmentPostRepository postRepository,
    IRecruitmentPostReadOnlyQueries recruitmentPostQueries,
    ITopicWriteRepository topicWriteRepository,
    ITopicReadOnlyQueries topicQueries,
    IMajorReadOnlyQueries majorQueries,
    IAiSemanticSearch semanticSearch,
    IAiLlmClient llmClient,
    ILogger<AiMatchingService> logger)
{
    private const int DefaultSuggestionLimit = 5;
    private const int MaxSuggestionLimit = 20;
    private const int OptionSuggestionLimit = 3;
    private const int RecruitmentScoreThreshold = 20;
    private const int RecruitmentScoreMax = 220;
    private const int TopicScoreThreshold = 10;
    private const int TopicScoreMax = 110;
    private const int TopicSkillCoverageWeight = 65;
    private const int ProfileScoreThreshold = 20;
    private const int ProfileScoreMax = 140;
    private const int SemanticShortlistLimit = 50;
    // Keep aligned with gateway MAX_SUGGESTIONS (currently 8) so every returned item can carry AI reason.
    private const int LlmCandidatePoolSize = 8;

    private readonly IAiSemanticSearch _semanticSearch = semanticSearch ?? throw new ArgumentNullException(nameof(semanticSearch));
    private readonly IAiLlmClient _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    private readonly ILogger<AiMatchingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiSummaryDto> GetSummaryAsync(Guid? semesterId, CancellationToken ct)
    {
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(semesterId, ct);

        var groupsWithoutTopic = await aiQueries.CountGroupsWithoutTopicAsync(semesterCtx.SemesterId, ct);
        var groupsUnderCapacity = await aiQueries.CountGroupsUnderCapacityAsync(semesterCtx.SemesterId, ct);
        var studentsWithoutGroup = await aiQueries.CountUnassignedStudentsAsync(semesterCtx.SemesterId, ct);

        return new AiSummaryDto(
            semesterCtx.SemesterId,
            semesterCtx.Name,
            DateTime.UtcNow,
            groupsWithoutTopic,
            groupsUnderCapacity,
            studentsWithoutGroup);
    }

    public async Task<AiOptionListDto> GetOptionsAsync(AiOptionRequest? request, CancellationToken ct)
    {
        request ??= new AiOptionRequest(null, AiOptionSection.All, 1, 20);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(request.SemesterId, ct);
        var (page, pageSize) = NormalizePagination(request.Page, request.PageSize);

        var majorLookup = (await majorQueries.ListAsync(ct)).ToDictionary(x => x.MajorId, x => x.MajorName);

        PaginatedCollection<GroupTopicOptionDto>? topicPage = null;
        PaginatedCollection<GroupStaffingOptionDto>? staffingPage = null;
        PaginatedCollection<StudentPlacementOptionDto>? studentPage = null;
        IReadOnlyList<NewGroupCreationOptionDto>? newGroupPlans = null;

        var section = request.Section;

        if (section is AiOptionSection.All or AiOptionSection.GroupsWithoutTopic)
        {
            var groups = await aiQueries.ListGroupsWithoutTopicAsync(semesterCtx.SemesterId, ct);
            var topics = await aiQueries.ListTopicAvailabilityAsync(semesterCtx.SemesterId, null, ct);
            topicPage = await BuildGroupTopicOptionsPageAsync(groups, topics, page, pageSize, ct);
        }

        if (section is AiOptionSection.All or AiOptionSection.GroupsNeedingMembers or AiOptionSection.StudentsWithoutGroup)
        {
            var groups = await aiQueries.ListGroupsUnderCapacityAsync(semesterCtx.SemesterId, ct);
            var students = await aiQueries.ListUnassignedStudentsAsync(semesterCtx.SemesterId, null, ct);
            var relatedGroupIds = groups.Select(g => g.GroupId).Distinct().ToArray();
            var mixSnapshots = relatedGroupIds.Length == 0
                ? new Dictionary<Guid, GroupRoleMixSnapshot>()
                : await aiQueries.GetGroupRoleMixAsync(relatedGroupIds, ct);

            var (groupOptions, studentOptions, remainingStudents) = BuildStaffingOptions(groups, students, mixSnapshots, majorLookup);

            if (section is AiOptionSection.All or AiOptionSection.GroupsNeedingMembers)
                staffingPage = Paginate(groupOptions, page, pageSize);

            if (section is AiOptionSection.All or AiOptionSection.StudentsWithoutGroup)
                studentPage = Paginate(studentOptions, page, pageSize);

            // Preview-only: propose how to create new groups for unassigned students (balanced by policy).
            // This must NOT write to DB.
            if (section is AiOptionSection.All or AiOptionSection.StudentsWithoutGroup)
            {
                var minSize = Math.Max(semesterCtx.Policy.DesiredGroupSizeMin, 1);
                var policyMax = semesterCtx.Policy.DesiredGroupSizeMax;
                if (policyMax <= 0)
                    policyMax = minSize;
                var maxSize = Math.Max(minSize, policyMax);

                var plans = new List<NewGroupCreationOptionDto>();
                foreach (var majorGroup in remainingStudents.GroupBy(s => s.MajorId).OrderBy(g => g.Key))
                {
                    var ordered = majorGroup
                        .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var sizes = ComputeBalancedGroupSizes(ordered.Count, minSize, maxSize);
                    var plannedCount = ordered.Count;
                    if (sizes.Count == 0)
                    {
                        // If we can't split ALL students into valid groups, still plan as many valid groups as possible,
                        // leaving the remainder unresolved.
                        for (var used = ordered.Count - 1; used >= minSize; used--)
                        {
                            var attempt = ComputeBalancedGroupSizes(used, minSize, maxSize);
                            if (attempt.Count > 0)
                            {
                                sizes = attempt;
                                plannedCount = used;
                                break;
                            }
                        }
                    }
                    var plannedGroups = new List<PlannedNewGroupDto>();
                    var unresolved = new List<StudentAssignmentIssueDto>();

                    var majorId = majorGroup.Key;
                    majorLookup.TryGetValue(majorId, out var majorName);

                    if (sizes.Count == 0)
                    {
                        foreach (var s in ordered)
                        {
                            unresolved.Add(new StudentAssignmentIssueDto(
                                s.UserId,
                                $"Không đủ sinh viên để tạo nhóm mới (policy min {minSize}, max {maxSize})."));
                        }
                    }
                    else
                    {
                        if (plannedCount < ordered.Count)
                        {
                            foreach (var s in ordered.Skip(plannedCount))
                            {
                                unresolved.Add(new StudentAssignmentIssueDto(
                                    s.UserId,
                                    $"Còn lại {ordered.Count - plannedCount} sinh viên chưa đủ để tạo nhóm mới (policy min {minSize}, max {maxSize})."));
                            }
                        }

                        var total = plannedCount;
                        var splitText = string.Join("+", sizes);
                        var baseReason = $"Đề xuất chia {total} sinh viên thành {sizes.Count} nhóm ({splitText}) để mọi nhóm đều nằm trong policy min {minSize}, max {maxSize}.";

                        var offset = 0;
                        for (var i = 0; i < sizes.Count; i++)
                        {
                            var size = sizes[i];
                            var batch = ordered.Take(plannedCount).Skip(offset).Take(size).ToList();
                            offset += size;

                            var studentsDetail = batch
                                .Select(s =>
                                {
                                    majorLookup.TryGetValue(s.MajorId, out var mname);
                                    var profile = BuildSkillProfile(s);
                                    return new PlannedNewGroupStudentDto(
                                        s.UserId,
                                        s.DisplayName,
                                        s.MajorId,
                                        mname,
                                        AiRoleHelper.ToDisplayString(profile.PrimaryRole),
                                        profile.Tags.Take(5).ToList());
                                })
                                .ToList();

                            plannedGroups.Add(new PlannedNewGroupDto(
                                Key: $"major_{majorId:N}_group_{i + 1}",
                                MajorId: majorId,
                                MajorName: majorName,
                                MemberCount: batch.Count,
                                Students: studentsDetail,
                                Reason: baseReason));
                        }
                    }

                    plans.Add(new NewGroupCreationOptionDto(
                        majorId,
                        majorName,
                        minSize,
                        maxSize,
                        plannedGroups,
                        unresolved));
                }

                newGroupPlans = plans;
            }

            // Enrich option reasons using AI.
            // Important: if caller requests StudentsWithoutGroup ONLY, staffingPage is null but we still need AI reasons/scores.
            if (staffingPage is null && studentPage is not null && studentPage.Items.Count > 0)
            {
                var neededGroupIds = studentPage.Items
                    .Where(s => s.SuggestedGroup is not null)
                    .Select(s => s.SuggestedGroup!.GroupId)
                    .Distinct()
                    .ToHashSet();

                var aiGroups = groupOptions
                    .Where(g => g.SuggestedMembers.Count > 0 && neededGroupIds.Contains(g.GroupId))
                    .ToList();

                var aiPageSize = Math.Max(1, aiGroups.Count);
                var staffingForAi = aiGroups.Count == 0
                    ? null
                    : new PaginatedCollection<GroupStaffingOptionDto>(aiGroups.Count, 1, aiPageSize, aiGroups);

                (_, studentPage) = await EnrichStaffingOptionReasonsWithAiAsync(staffingForAi, studentPage, ct);
            }
            else
            {
                (staffingPage, studentPage) = await EnrichStaffingOptionReasonsWithAiAsync(staffingPage, studentPage, ct);
            }
        }

        return new AiOptionListDto(
            semesterCtx.SemesterId,
            semesterCtx.Name,
            section,
            topicPage,
            staffingPage,
            studentPage,
            newGroupPlans);
    }

    public async Task<IReadOnlyList<RecruitmentPostSuggestionDto>> SuggestRecruitmentPostsForStudentAsync(
        Guid studentId,
        RecruitmentPostSuggestionRequest? request,
        CancellationToken ct)
    {
        request ??= new RecruitmentPostSuggestionRequest(null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(null, ct);
        EnsureWindow(DateOnly.FromDateTime(DateTime.UtcNow),
            semesterCtx.Policy.TeamSuggestStart,
            semesterCtx.Policy.TeamSelfSelectEnd,
            "Thời gian tự ghép nhóm đã đóng, không thể dùng gợi ý AI.");

        if (await groupQueries.HasActiveGroupAsync(studentId, semesterCtx.SemesterId, ct))
            throw new InvalidOperationException("Bạn đã có nhóm trong học kỳ này.");

        var profile = await aiQueries.GetStudentProfileAsync(studentId, semesterCtx.SemesterId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy hồ sơ sinh viên trong danh sách ghép tự động.");

        var studentSkills = BuildSkillProfile(profile);
        Guid? targetMajorId = request.MajorId ?? profile.MajorId;
        var posts = await aiQueries.ListOpenRecruitmentPostsAsync(semesterCtx.SemesterId, targetMajorId, ct);
        if (posts.Count == 0)
            return Array.Empty<RecruitmentPostSuggestionDto>();

        var semanticQuery = BuildSemanticQuery(studentSkills);
        var filteredPosts = await ApplySemanticShortlistAsync("recruitment_post", semanticQuery, semesterCtx.SemesterId, targetMajorId, posts, p => p.PostId, ct);
        if (filteredPosts.Count == 0)
            return Array.Empty<RecruitmentPostSuggestionDto>();

        var limit = NormalizeLimit(request.Limit);
        var poolSize = Math.Min(filteredPosts.Count, Math.Max(limit, LlmCandidatePoolSize));
        var suggestions = filteredPosts
            .Select(post => BuildPostSuggestion(studentSkills, profile.MajorId, post))
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .OrderByDescending(dto => dto.Score)
            .Take(poolSize)
            .ToList();

        if (suggestions.Count == 0)
            return suggestions;

        var queryText = BuildStudentQueryText(profile, studentSkills, targetMajorId);
        var context = BuildLlmContext(
            ("semesterId", semesterCtx.SemesterId.ToString()),
            ("studentId", studentId.ToString()),
            ("targetMajorId", targetMajorId?.ToString()),
            ("mode", "group_post"),
            ("topN", suggestions.Count.ToString(CultureInfo.InvariantCulture)),
            ("desiredPosition", profile.DesiredPositionName),
            ("mustMentionDesiredPosition", "true")
        );

        var reranked = await ApplyLlmRerankAsync(
            "recruitment_post",
            queryText,
            suggestions,
            s => s.PostId,
            BuildRecruitmentCandidate,
            ApplyRecruitmentRerank,
            s => s.Score,
            context,
                requireAiReason: true,
            ct);

        var finalSuggestions = reranked
            .Take(limit)
            .ToList();

        if (finalSuggestions.Count == 0)
            return finalSuggestions;

        return await HydrateRecruitmentPostSuggestionsAsync(finalSuggestions, studentId, ct);
    }

    public async Task<IReadOnlyList<TopicSuggestionDto>> SuggestTopicsForGroupAsync(
        Guid currentUserId,
        TopicSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        return await SuggestTopicsInternalAsync(request.GroupId, currentUserId, enforceMembership: true, request.Limit, ct);
    }

    private async Task<IReadOnlyList<TopicSuggestionDto>> SuggestTopicsInternalAsync(
        Guid groupId,
        Guid currentUserId,
        bool enforceMembership,
        int? limit,
        CancellationToken ct)
    {
        var detail = await groupQueries.GetGroupAsync(groupId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm.");

        if (enforceMembership)
        {
            var isMember = await groupQueries.IsActiveMemberAsync(groupId, currentUserId, ct);
            if (!isMember)
                throw new UnauthorizedAccessException("Chỉ thành viên nhóm mới được xem gợi ý topic.");
        }

        var semesterCtx = await ResolveSemesterAsync(detail.SemesterId, ct);
        EnsureWindow(DateOnly.FromDateTime(DateTime.UtcNow),
            semesterCtx.Policy.TopicSuggestStart,
            semesterCtx.Policy.TopicSelfSelectEnd,
            "Đã quá hạn tự chọn topic.");

        if (!detail.MajorId.HasValue)
            throw new InvalidOperationException("Nhóm chưa có chuyên ngành xác định.");

        var members = await aiQueries.ListGroupMemberSkillsAsync(groupId, ct);
        if (members.Count == 0)
            throw new InvalidOperationException("Nhóm cần ít nhất một thành viên có hồ sơ kỹ năng để gợi ý.");

        var memberProfiles = BuildMemberSkillProfiles(members);
        var groupSkillProfile = BuildGroupSkillProfile(detail.Skills, memberProfiles);

        var available = await aiQueries.ListTopicAvailabilityAsync(detail.SemesterId, detail.MajorId, ct);
        if (available.Count == 0)
            return Array.Empty<TopicSuggestionDto>();

        var semanticQuery = BuildSemanticQuery(groupSkillProfile);
        var candidates = await ApplySemanticShortlistAsync("topic", semanticQuery, detail.SemesterId, detail.MajorId, available, t => t.TopicId, ct);
        if (candidates.Count == 0)
            return Array.Empty<TopicSuggestionDto>();

        // Topic suggestions: luôn trả 4-6 items (default 5) để UX ổn định và có reason từ AI.
        var limitValue = NormalizeTopicSuggestionLimit(limit);
        var poolSize = Math.Min(candidates.Count, Math.Max(limitValue, LlmCandidatePoolSize));
        var items = candidates
            .Select(topic => BuildTopicSuggestion(topic, groupSkillProfile))
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .OrderByDescending(dto => dto.Score)
            .Take(poolSize)
            .ToList();

        if (items.Count == 0)
            return items;

        var queryText = BuildGroupQueryText(detail.Name, groupSkillProfile, null);
        var context = BuildLlmContext(
            ("semesterId", detail.SemesterId.ToString()),
            ("groupId", groupId.ToString()),
            ("majorId", detail.MajorId?.ToString()),
            ("mode", "topic"),
            ("topN", items.Count.ToString(CultureInfo.InvariantCulture)));

        var reranked = await ApplyLlmRerankAsync(
            "topic",
            queryText,
            items,
            s => s.TopicId,
            BuildTopicCandidate,
            ApplyTopicRerank,
            s => s.Score,
            context,
            requireAiReason: true,
            ct);

        var finalItems = reranked
            .Take(limitValue)
            .ToList();

        if (finalItems.Count == 0)
            return finalItems;

        return await HydrateTopicSuggestionsAsync(finalItems, ct);
    }

    public async Task<IReadOnlyList<ProfilePostSuggestionDto>> SuggestProfilePostsForGroupAsync(
        Guid currentUserId,
        ProfilePostSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var detail = await groupQueries.GetGroupAsync(request.GroupId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm.");

        var isMember = await groupQueries.IsActiveMemberAsync(request.GroupId, currentUserId, ct);
        if (!isMember)
            throw new UnauthorizedAccessException("Chỉ thành viên nhóm mới được xem gợi ý.");

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(request.GroupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Nhóm đã đủ thành viên.");

        var semesterCtx = await ResolveSemesterAsync(detail.SemesterId, ct);
        EnsureWindow(DateOnly.FromDateTime(DateTime.UtcNow),
            semesterCtx.Policy.TeamSuggestStart,
            semesterCtx.Policy.TeamSelfSelectEnd,
            "Đã quá hạn tự ghép nhóm.");

        if (!detail.MajorId.HasValue)
            throw new InvalidOperationException("Nhóm chưa có chuyên ngành xác định.");

        var members = await aiQueries.ListGroupMemberSkillsAsync(request.GroupId, ct);
        var memberProfiles = BuildMemberSkillProfiles(members);
        var groupSkillProfile = BuildGroupSkillProfile(detail.Skills, memberProfiles);

        var openRecruitmentPosts = await aiQueries.ListOpenRecruitmentPostsAsync(detail.SemesterId, detail.MajorId, ct);
        var now = DateTime.UtcNow;
        var groupRecruitmentPosts = openRecruitmentPosts
            .Where(p => p.GroupId.HasValue && p.GroupId.Value == request.GroupId)
            .Where(p => p.ApplicationDeadline is null || p.ApplicationDeadline.Value >= now)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        var needsProfile = BuildRecruitmentNeedProfile(groupRecruitmentPosts);

        var mixes = await aiQueries.GetGroupRoleMixAsync(new[] { request.GroupId }, ct);
        var mix = mixes.TryGetValue(request.GroupId, out var snapshot)
            ? snapshot
            : new GroupRoleMixSnapshot(request.GroupId, 0, 0, 0);

        var posts = await aiQueries.ListOpenProfilePostsAsync(detail.SemesterId, detail.MajorId, ct);
        if (posts.Count == 0)
            return Array.Empty<ProfilePostSuggestionDto>();

        // If the group has no recruitment posts, we should still compare all profile posts
        // against the group's current needs (role mix + skills), not a recruitment-only signal.
        IReadOnlyList<ProfilePostSnapshot> filteredPosts;
        if (groupRecruitmentPosts.Count == 0)
        {
            filteredPosts = posts;
        }
        else
        {
            var semanticQuery = BuildSemanticQuery(needsProfile, groupSkillProfile);
            filteredPosts = await ApplySemanticShortlistAsync("profile_post", semanticQuery, detail.SemesterId, detail.MajorId, posts, p => p.PostId, ct);
            if (filteredPosts.Count == 0)
                return Array.Empty<ProfilePostSuggestionDto>();
        }

        var limit = NormalizeLimit(request.Limit);
        var poolSize = Math.Min(filteredPosts.Count, Math.Max(limit, LlmCandidatePoolSize));
        var suggestions = filteredPosts
            .Select(post => BuildProfilePostSuggestion(post, needsProfile, groupSkillProfile, mix))
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .OrderByDescending(dto => dto.Score)
            .Take(poolSize)
            .ToList();

        if (suggestions.Count == 0)
            return suggestions;

        var queryText = BuildGroupQueryText(detail.Name, needsProfile, groupSkillProfile, groupRecruitmentPosts, mix);
        var context = BuildLlmContext(
            ("semesterId", detail.SemesterId.ToString()),
            ("groupId", detail.Id.ToString()),
            ("majorId", detail.MajorId?.ToString()));

        // Provide a structured payload for the AI gateway/host to understand the team context for personal-post rerank.
        // This is sent via context so it remains backward compatible with the existing gateway contract.
        var needsText = BuildRecruitmentNeedsText(groupRecruitmentPosts);
        var teamJson = BuildGroupPostTeamContext(detail.Name, maxMembers - activeCount, mix, groupSkillProfile, needsProfile, needsText);
        context = BuildLlmContext(
            ("semesterId", detail.SemesterId.ToString()),
            ("groupId", detail.Id.ToString()),
            ("majorId", detail.MajorId?.ToString()),
            ("mode", "personal_post"),
            ("topN", suggestions.Count.ToString(CultureInfo.InvariantCulture)),
            ("team", teamJson));

        var reranked = await ApplyLlmRerankAsync(
            "profile_post",
            queryText,
            suggestions,
            s => s.PostId,
            BuildProfileCandidate,
            ApplyProfileRerank,
            s => s.Score,
            context,
                requireAiReason: true,
            ct);

        var finalSuggestions = reranked
            .Take(limit)
            .ToList();

        if (finalSuggestions.Count == 0)
            return finalSuggestions;

        return await HydrateProfilePostSuggestionsAsync(finalSuggestions, currentUserId, ct);
    }

    private static AiSkillProfile BuildRecruitmentNeedProfile(IReadOnlyList<RecruitmentPostSnapshot> posts)
    {
        if (posts.Count == 0)
            return AiSkillProfile.Empty;

        var sources = new List<AiSkillProfile>();
        foreach (var post in posts)
        {
            var required = AiSkillProfile.FromJson(post.RequiredSkills);
            var position = AiSkillProfile.FromText(post.PositionNeeded);

            if (required.HasTags || required.PrimaryRole != AiPrimaryRole.Unknown)
                sources.Add(required);
            if (position.HasTags || position.PrimaryRole != AiPrimaryRole.Unknown)
                sources.Add(position);
        }

        return sources.Count == 0 ? AiSkillProfile.Empty : AiSkillProfile.Combine(sources);
    }

    private static string BuildRecruitmentNeedsText(IReadOnlyList<RecruitmentPostSnapshot> posts)
    {
        if (posts.Count == 0)
            return string.Empty;

        var positions = posts
            .Select(p => p.PositionNeeded)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var skillTags = posts
            .Select(p => AiSkillProfile.FromJson(p.RequiredSkills))
            .Where(p => p.HasTags)
            .SelectMany(p => p.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        var parts = new List<string>();
        if (positions.Count > 0)
            parts.Add("Position needed: " + string.Join(", ", positions));
        if (skillTags.Count > 0)
            parts.Add("Required skills: " + string.Join(", ", skillTags));
        return string.Join(" - ", parts);
    }

    public async Task<AutoAssignTeamsResultDto> AutoAssignTeamsAsync(Guid currentUserId, AutoAssignTeamsRequest? request, CancellationToken ct)
    {
        request ??= new AutoAssignTeamsRequest(null, null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(request.SemesterId, ct);
        var phase = await AssignStudentsToGroupsAsync(semesterCtx, request.MajorId, request.Limit, ct);
        var majorLookup = (await majorQueries.ListAsync(ct)).ToDictionary(x => x.MajorId, x => x.MajorName);
        var newGroups = await CreateNewGroupsForStudentsAsync(phase.RemainingStudents, semesterCtx, currentUserId, request.MajorId, majorLookup, ct);

        var combinedAssignments = phase.Assignments.Concat(newGroups.Assignments).ToList();

        if (combinedAssignments.Count > 0)
            await RefreshAssignmentCachesAsync(ct);

        return new AutoAssignTeamsResultDto(
            combinedAssignments.Count,
            combinedAssignments,
            newGroups.UnresolvedStudents.Select(x => x.StudentId).ToList(),
            newGroups.UnresolvedStudents,
            phase.OpenGroupIds,
            phase.GroupIssues,
            newGroups.Groups.Count,
            newGroups.Groups);
    }

    public async Task<AutoAssignTopicBatchResultDto> AutoAssignTopicAsync(
        Guid currentUserId,
        bool canManageAllGroups,
        AutoAssignTopicRequest? request,
        CancellationToken ct)
    {
        request ??= new AutoAssignTopicRequest(null, null, null);

        if (request.GroupId.HasValue)
        {
            var attempt = await AssignTopicToGroupAsync(request.GroupId.Value, currentUserId, enforceMembership: true, request.LimitPerGroup, throwIfUnavailable: true, ct);
            return new AutoAssignTopicBatchResultDto(1, new[] { attempt.Assignment! }, Array.Empty<Guid>(), Array.Empty<TopicAssignmentIssueDto>());
        }

        if (!canManageAllGroups)
            throw new UnauthorizedAccessException("Chỉ admin hoặc moderator mới được phép auto assign cho toàn bộ nhóm.");

        var semesterId = await groupQueries.GetActiveSemesterIdAsync(ct)
            ?? throw new InvalidOperationException("Không tìm thấy học kỳ.");

        var phase = await AssignTopicsForEligibleGroupsAsync(semesterId, request.MajorId, currentUserId, request.LimitPerGroup, ct);
        return new AutoAssignTopicBatchResultDto(phase.Assignments.Count, phase.Assignments, phase.SkippedGroupIds, phase.Issues);
    }

    public async Task<AiAutoResolveResultDto> AutoResolveAsync(
        Guid currentUserId,
        AiAutoResolveRequest? request,
        CancellationToken ct)
    {
        request ??= new AiAutoResolveRequest(null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(request.SemesterId, ct);

        var studentPhase = await AssignStudentsToGroupsAsync(semesterCtx, request.MajorId, null, ct);
        var majorLookup = (await majorQueries.ListAsync(ct)).ToDictionary(x => x.MajorId, x => x.MajorName);
        var newGroupsPhase = await CreateNewGroupsForStudentsAsync(studentPhase.RemainingStudents, semesterCtx, currentUserId, request.MajorId, majorLookup, ct);
        var topicPhase = await AssignTopicsForEligibleGroupsAsync(semesterCtx.SemesterId, request.MajorId, currentUserId, null, ct);

        var totalAssignments = studentPhase.Assignments.Count + newGroupsPhase.Assignments.Count;
        var totalTopics = topicPhase.Assignments.Count;
        var combinedAssignments = studentPhase.Assignments.Concat(newGroupsPhase.Assignments).ToList();
        var combinedTopicAssignments = topicPhase.Assignments.ToList();
        var skippedTopics = topicPhase.SkippedGroupIds.ToList();

        if (totalAssignments > 0)
            await RefreshAssignmentCachesAsync(ct);

        return new AiAutoResolveResultDto(
            semesterCtx.SemesterId,
            semesterCtx.Name,
            totalAssignments,
            totalTopics,
            newGroupsPhase.Groups.Count,
            combinedAssignments,
            combinedTopicAssignments,
            skippedTopics,
            studentPhase.GroupIssues,
            newGroupsPhase.Groups,
            newGroupsPhase.UnresolvedStudents.Select(x => x.StudentId).ToList(),
            newGroupsPhase.UnresolvedStudents);
    }

    private async Task<StudentAssignmentPhaseResult> AssignStudentsToGroupsAsync(
        SemesterContext semesterCtx,
        Guid? majorId,
        int? requestLimit,
        CancellationToken ct)
    {
        var students = await aiQueries.ListUnassignedStudentsAsync(semesterCtx.SemesterId, majorId, ct);
        if (students.Count == 0)
            return new StudentAssignmentPhaseResult(Array.Empty<AutoAssignmentRecordDto>(), Array.Empty<StudentProfileSnapshot>(), Array.Empty<Guid>(), Array.Empty<GroupAssignmentIssueDto>());

        var groups = await aiQueries.ListGroupCapacitiesAsync(semesterCtx.SemesterId, majorId, ct);
        if (groups.Count == 0)
            return new StudentAssignmentPhaseResult(Array.Empty<AutoAssignmentRecordDto>(), students, Array.Empty<Guid>(), Array.Empty<GroupAssignmentIssueDto>());

        var mixes = await aiQueries.GetGroupRoleMixAsync(groups.Select(g => g.GroupId), ct);
        var limitConfigured = requestLimit.HasValue && requestLimit.Value > 0;
        var limitRemaining = limitConfigured ? requestLimit!.Value : int.MaxValue;
        var limitHit = false;
        var policyMinSize = Math.Max(semesterCtx.Policy.DesiredGroupSizeMin, 1);
        var rawPolicyMax = semesterCtx.Policy.DesiredGroupSizeMax;
        var policyMaxSize = rawPolicyMax <= 0 ? policyMinSize : Math.Max(rawPolicyMax, policyMinSize);

        var studentsByMajor = students
            .GroupBy(s => s.MajorId)
            .ToDictionary(g => g.Key, g => new RolePools(g));

        var groupsByMajor = groups
            .Where(g => g.MajorId.HasValue)
            .GroupBy(g => g.MajorId!.Value)
            .ToDictionary(g => g.Key, g => g
                .Select(item => new GroupAssignmentState(item, mixes.TryGetValue(item.GroupId, out var mix)
                    ? mix
                    : new GroupRoleMixSnapshot(item.GroupId, 0, 0, 0)))
                .OrderByDescending(x => x.RemainingSlots)
                .ToList());

        var assignments = new List<AutoAssignmentRecordDto>();

        // If AI gateway is enabled, use it to pick the best next student for each group.
        // Hard constraints (major, capacity, policy range) remain enforced here.
        var useAi = _llmClient is not null;

        foreach (var (majorKey, groupStates) in groupsByMajor)
        {
            if (!studentsByMajor.TryGetValue(majorKey, out var pools))
                continue;

            foreach (var groupState in groupStates)
            {
                while (limitRemaining > 0)
                {
                    if (groupState.RemainingSlots == 0)
                    {
                        var available = pools.RemainingCount;
                        if (available == 0)
                            break;

                        var slotsNeeded = Math.Min(limitRemaining, available);
                        if (slotsNeeded <= 0)
                            break;

                        var policyCap = Math.Max(policyMaxSize, groupState.MaxMembers);
                        if (!groupState.TryExpand(policyCap, slotsNeeded))
                            break;
                    }

                    CandidateSelection? candidate;
                    if (useAi)
                    {
                        candidate = await DequeueForGroupWithAiAsync(groupState, pools, ct);
                    }
                    else
                    {
                        candidate = pools.DequeueForGroup(groupState);
                    }
                    if (candidate is null)
                        break;

                    await groupRepository.AddMembershipAsync(groupState.GroupId, candidate.UserId, groupState.SemesterId, "member", ct);
                    await postRepository.DeleteProfilePostsForUserAsync(candidate.UserId, groupState.SemesterId, ct);
                    assignments.Add(new AutoAssignmentRecordDto(
                        candidate.UserId,
                        groupState.GroupId,
                        groupState.Name,
                        AiRoleHelper.ToDisplayString(candidate.Role)));

                    var isHighGpa = pools.IsHighGpa(candidate.Profile);
                    groupState.Apply(candidate.Role, isHighGpa);
                    limitRemaining--;
                    if (limitConfigured && limitRemaining == 0)
                        limitHit = true;
                }
            }
        }

        var remainingIds = studentsByMajor.Values
            .SelectMany(p => p.RemainingStudentIds)
            .Distinct()
            .ToList();
        var remainingSet = new HashSet<Guid>(remainingIds);
        var remainingSnapshots = students.Where(s => remainingSet.Contains(s.UserId)).ToList();

        var states = groupsByMajor.Values.SelectMany(g => g).ToList();
        foreach (var state in states)
            state.EnsurePolicyRange(policyMinSize, policyMaxSize);

        foreach (var state in states)
        {
            if (state.RemainingSlots == 0)
                continue;

            RolePools? pools = null;
            if (state.MajorId.HasValue)
                studentsByMajor.TryGetValue(state.MajorId.Value, out pools);

            var hasCandidates = (pools?.RemainingCount ?? 0) > 0;
            if (!hasCandidates)
                state.ShrinkToRange(policyMinSize, policyMaxSize);
        }

        var openStates = states.Where(s => s.RemainingSlots > 0).ToList();
        var openGroups = openStates.Select(state => state.GroupId).Distinct().ToList();

        var groupIssues = new List<GroupAssignmentIssueDto>(openStates.Count);
        foreach (var state in openStates)
        {
            RolePools? pools = null;
            if (state.MajorId.HasValue)
                studentsByMajor.TryGetValue(state.MajorId.Value, out pools);

            var reason = BuildGroupIssueReason(state, pools, limitConfigured, limitHit, requestLimit, Math.Max(policyMaxSize, state.MaxMembers));
            groupIssues.Add(new GroupAssignmentIssueDto(state.GroupId, reason));
        }

        var capacityChanges = states
            .Where(state => state.CapacityDirty)
            .ToList();

        foreach (var state in capacityChanges)
        {
            await groupRepository.UpdateGroupAsync(state.GroupId, null, null, state.MaxMembers, null, null, null, null, ct);
        }

        // Auto-assign team DOES NOT pick topic/mentor. It may only activate groups
        // that are already full AND already have topic+mentor.
        foreach (var state in states)
        {
            if (state.CurrentMembers < state.MaxMembers)
                continue;

            await TryActivateGroupIfReadyAsync(state.GroupId, state.CurrentMembers, state.MaxMembers, ct);
        }

        return new StudentAssignmentPhaseResult(assignments, remainingSnapshots, openGroups, groupIssues);
    }

    private async Task<CandidateSelection?> DequeueForGroupWithAiAsync(
        GroupAssignmentState groupState,
        RolePools pools,
        CancellationToken ct)
    {
        // Keep the candidate pool small and DB-only; this is used only for choosing the NEXT student.
        // The final assignment is still executed/validated server-side.
        var pool = new List<CandidateSelection>(capacity: 18);

        void AddFromQueue(IEnumerable<StudentProfileSnapshot> students, AiPrimaryRole role, int max)
        {
            foreach (var s in students.Take(max))
            {
                pool.Add(new CandidateSelection(s, role));
            }
        }

        // Bias the sample toward roles the group lacks.
        var needFe = !groupState.HasFrontend;
        var needBe = !groupState.HasBackend;

        if (needFe)
            AddFromQueue(pools.PeekFrontend(8), AiPrimaryRole.Frontend, 8);
        if (needBe)
            AddFromQueue(pools.PeekBackend(8), AiPrimaryRole.Backend, 8);

        // Always add some others as backup.
        AddFromQueue(pools.PeekOther(6), AiPrimaryRole.Other, 6);

        if (pool.Count == 0)
            return null;

        // GPA balancing: if this group already got a "high GPA" member,
        // try to avoid selecting another high-GPA member when alternatives exist.
        if (groupState.HighGpaAssignedCount > 0 && pools.HighGpaThreshold.HasValue)
        {
            var threshold = pools.HighGpaThreshold.Value;
            var hasNonHigh = pool.Any(c => c.Profile.Gpa.HasValue && c.Profile.Gpa.Value < threshold);
            if (hasNonHigh)
                pool = pool.Where(c => !c.Profile.Gpa.HasValue || c.Profile.Gpa.Value < threshold).ToList();
        }

        var candidates = pool.Select((c, i) =>
        {
            var tags = BuildSkillProfile(c.Profile).Tags.Take(8).ToList();
            var title = c.Profile.DisplayName;
            var gpaText = BuildGpa10Line(c.Profile.Gpa);
            var desiredPositionText = string.IsNullOrWhiteSpace(c.Profile.DesiredPositionName)
                ? null
                : $"Desired position: {c.Profile.DesiredPositionName}.";
            var text = BuildStructuredCandidateText(
                title,
                $"Role: {AiRoleHelper.ToDisplayString(AiRoleHelper.Parse(c.Profile.PrimaryRole))}.",
                tags,
                null,
                null,
                null,
                extraLines: new[] { gpaText, desiredPositionText });

            // neededRole is what the group needs next.
            var neededRole = needFe ? "frontend" : (needBe ? "backend" : "other");

            var metadata = BuildMetadata(
                ("neededRole", neededRole),
                ("score", "0"),
                ("groupFrontend", groupState.FrontendCount.ToString(CultureInfo.InvariantCulture)),
                ("groupBackend", groupState.BackendCount.ToString(CultureInfo.InvariantCulture)),
                ("groupOther", groupState.OtherCount.ToString(CultureInfo.InvariantCulture)));

            return new AiLlmCandidate(
                $"s{i + 1:00}",
                c.Profile.UserId,
                title,
                null,
                text,
                metadata);
        }).ToList();

        // Query text describes the group needs.
        var queryText = $"Auto-assign to group '{groupState.Name}'. Need role: {(needFe ? "frontend" : needBe ? "backend" : "other")}.";

        var context = BuildLlmContext(
            ("mode", "auto_assign_team"),
            ("withReasons", "false"),
            ("topN", "1"),
            ("groupId", groupState.GroupId.ToString()));

        AiLlmRerankResponse response;
        try
        {
            response = await _llmClient.RerankAsync(new AiLlmRerankRequest(
                "auto_assign_team",
                queryText,
                candidates,
                context), ct);
        }
        catch
        {
            // Fail soft: revert to deterministic selection.
            return pools.DequeueForGroup(groupState);
        }

        var bestKey = response.Items?.OrderByDescending(x => x.FinalScore).FirstOrDefault()?.Key;
        if (string.IsNullOrWhiteSpace(bestKey))
            return pools.DequeueForGroup(groupState);

        var idx = int.TryParse(bestKey.AsSpan(1), out var keyNum) ? keyNum - 1 : -1;
        if (idx < 0 || idx >= pool.Count)
            return pools.DequeueForGroup(groupState);

        // Now actually dequeue that specific student from pools.
        var chosen = pool[idx];
        return pools.DequeueSpecific(chosen.UserId) ?? pools.DequeueForGroup(groupState);
    }

    private async Task<TopicAssignmentPhaseResult> AssignTopicsForEligibleGroupsAsync(
        Guid semesterId,
        Guid? majorId,
        Guid actorUserId,
        int? limitPerGroup,
        CancellationToken ct)
    {
        var groups = await groupQueries.ListGroupsAsync(null, majorId, null, ct);
        var targets = groups
            .Where(g => g.Topic is null
                        && g.Semester.SemesterId == semesterId
                        )
            .Select(g => g.Id)
            .Distinct()
            .ToList();

        if (targets.Count == 0)
            return new TopicAssignmentPhaseResult(Array.Empty<AutoAssignTopicResultDto>(), Array.Empty<Guid>(), Array.Empty<TopicAssignmentIssueDto>());

        var assignments = new List<AutoAssignTopicResultDto>();
        var skipped = new List<Guid>();
        var issues = new List<TopicAssignmentIssueDto>();

        foreach (var groupId in targets)
        {
            try
            {
                var attempt = await AssignTopicToGroupAsync(groupId, actorUserId, enforceMembership: false, limitPerGroup, throwIfUnavailable: false, ct);
                if (attempt.Assignment is not null)
                {
                    assignments.Add(attempt.Assignment);
                }
                else
                {
                    skipped.Add(groupId);
                    if (!string.IsNullOrWhiteSpace(attempt.FailureReason))
                        issues.Add(new TopicAssignmentIssueDto(groupId, attempt.FailureReason!));
                }
            }
            catch (Exception ex)
            {
                skipped.Add(groupId);
                issues.Add(new TopicAssignmentIssueDto(groupId, ex.Message));
            }
        }

        return new TopicAssignmentPhaseResult(assignments, skipped, issues);
    }

    private async Task<PaginatedCollection<GroupTopicOptionDto>> BuildGroupTopicOptionsPageAsync(
        IReadOnlyList<GroupOverviewSnapshot> groups,
        IReadOnlyList<TopicAvailabilitySnapshot> topics,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var total = groups.Count;
        if (total == 0)
            return new PaginatedCollection<GroupTopicOptionDto>(0, page, pageSize, Array.Empty<GroupTopicOptionDto>());

        var skip = (page - 1) * pageSize;
        var orderedGroups = groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var pageGroups = orderedGroups.Skip(skip).Take(pageSize).ToList();

        var topicList = topics.ToList();
        var items = new List<GroupTopicOptionDto>(pageGroups.Count);

        foreach (var group in pageGroups)
        {
            var groupProfile = await GetGroupSkillProfileAsync(group.GroupId, ct);
            var groupQueryText = BuildGroupQueryText(group.Name, groupProfile, null);
            var semanticQuery = BuildSemanticQuery(groupProfile);
            var relevantTopics = group.MajorId.HasValue
                ? topicList.Where(t => !t.MajorId.HasValue || t.MajorId.Value == group.MajorId.Value)
                : topicList;

            // Match SuggestTopics pipeline: use vector shortlist before rerank.
            var candidateTopics = await ApplySemanticShortlistAsync(
                "topic",
                semanticQuery,
                group.SemesterId,
                group.MajorId,
                relevantTopics.ToList(),
                t => t.TopicId,
                ct);

            // Build heuristic shortlist then ask AI to produce reasons (and optionally adjust ordering).
            var shortlist = candidateTopics
                .Select(topic => BuildTopicSuggestion(topic, groupProfile))
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderByDescending(s => s.Score)
                .Take(LlmCandidatePoolSize)
                .ToList();

            var reranked = await ApplyLlmRerankAsync(
                "topic_options",
                groupQueryText,
                shortlist,
                s => s.TopicId,
                BuildTopicCandidate,
                ApplyTopicRerank,
                s => s.Score,
                context: BuildLlmContext(
                    ("groupId", group.GroupId.ToString()),
                    ("mode", "topic"),
                    ("topN", shortlist.Count.ToString(CultureInfo.InvariantCulture))),
                requireAiReason: true,
                ct);

            var suggestionItems = reranked
                .Take(OptionSuggestionLimit)
                .Select(s => new TopicSuggestionDetailDto(
                    s.TopicId,
                    s.Title,
                    s.Description,
                    s.Score,
                    s.MatchingSkills,
                    s.AiReason ?? BuildTopicReason(topics.First(t => t.TopicId == s.TopicId), s, group)))
                .ToList();

            items.Add(new GroupTopicOptionDto(
                group.GroupId,
                group.Name,
                group.Description,
                group.MajorId,
                group.MajorName,
                group.MaxMembers,
                group.CurrentMembers,
                group.RemainingSlots,
                suggestionItems.Count == 0 ? 0 : suggestionItems.Max(s => s.Score),
                suggestionItems));
        }

        return new PaginatedCollection<GroupTopicOptionDto>(total, page, pageSize, items);
    }

    private async Task<AiSkillProfile> GetGroupSkillProfileAsync(Guid groupId, CancellationToken ct)
    {
        var detail = await groupQueries.GetGroupAsync(groupId, ct);
        var members = await aiQueries.ListGroupMemberSkillsAsync(groupId, ct);
        if ((detail?.Skills is null || detail.Skills.Count == 0) && members.Count == 0)
            return AiSkillProfile.Empty;

        var memberProfiles = BuildMemberSkillProfiles(members);
        return BuildGroupSkillProfile(detail?.Skills, memberProfiles);
    }

    private (IReadOnlyList<GroupStaffingOptionDto> Groups, IReadOnlyList<StudentPlacementOptionDto> Students, IReadOnlyList<StudentProfileSnapshot> RemainingStudents) BuildStaffingOptions(
        IReadOnlyList<GroupOverviewSnapshot> groups,
        IReadOnlyList<StudentProfileSnapshot> students,
        IReadOnlyDictionary<Guid, GroupRoleMixSnapshot> mixes,
        IReadOnlyDictionary<Guid, string> majors)
    {
        if (groups.Count == 0 && students.Count == 0)
            return (Array.Empty<GroupStaffingOptionDto>(), Array.Empty<StudentPlacementOptionDto>(), Array.Empty<StudentProfileSnapshot>());

        var highGpaThresholdByMajor = ComputeHighGpaThresholdByMajor(students);

        var candidateStates = students
            .Select(s => new StudentCandidate(s, BuildSkillProfile(s), GetMajorName(s.MajorId, majors)))
            .ToDictionary(c => c.Snapshot.UserId, c => c);

        var availablePool = candidateStates.Values.ToList();
        var placements = new Dictionary<Guid, GroupPlacementSuggestionDto>();
        var groupOptions = new List<GroupStaffingOptionDto>(groups.Count);

        foreach (var group in groups.OrderByDescending(g => g.RemainingSlots).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var mix = mixes.TryGetValue(group.GroupId, out var snapshot)
                ? snapshot
                : new GroupRoleMixSnapshot(group.GroupId, 0, 0, 0);

            var mutableMix = mix;
            var slots = group.RemainingSlots;
            var suggestions = new List<GroupCandidateSuggestionDto>();

            double? highThreshold = group.MajorId.HasValue && highGpaThresholdByMajor.TryGetValue(group.MajorId.Value, out var threshold)
                ? threshold
                : null;
            var assignedHighCount = 0;

            while (slots > 0 && availablePool.Count > 0)
            {
                var (index, score) = (assignedHighCount > 0 && highThreshold.HasValue)
                    ? FindBestCandidate(
                        availablePool,
                        group,
                        mutableMix,
                        extraFilter: c => !c.Snapshot.Gpa.HasValue || c.Snapshot.Gpa.Value < highThreshold.Value)
                    : FindBestCandidate(availablePool, group, mutableMix);

                // If we filtered out high GPA but there was no eligible candidate, fallback to normal selection.
                if (index < 0 && assignedHighCount > 0 && highThreshold.HasValue)
                    (index, score) = FindBestCandidate(availablePool, group, mutableMix);
                if (index < 0)
                    break;

                var candidate = availablePool[index];
                availablePool.RemoveAt(index);

                if (highThreshold.HasValue && candidate.Snapshot.Gpa.HasValue && candidate.Snapshot.Gpa.Value >= highThreshold.Value)
                    assignedHighCount++;

                var reason = BuildCandidateReason(group, mutableMix, candidate.Profile, candidate.Snapshot, score);
                suggestions.Add(new GroupCandidateSuggestionDto(
                    candidate.Snapshot.UserId,
                    candidate.Snapshot.DisplayName,
                    candidate.Snapshot.MajorId,
                    candidate.MajorName,
                    candidate.Snapshot.Gpa,
                    candidate.Snapshot.DesiredPositionName,
                    AiRoleHelper.ToDisplayString(candidate.Profile.PrimaryRole),
                    candidate.Profile.Tags.Take(5).ToList(),
                    score,
                    reason));

                placements[candidate.Snapshot.UserId] = new GroupPlacementSuggestionDto(
                    group.GroupId,
                    group.Name,
                    group.MajorId,
                    group.MajorName,
                    score,
                    reason);

                mutableMix = ApplyRoleToMix(mutableMix, candidate.Profile.PrimaryRole);
                slots--;
            }

            groupOptions.Add(new GroupStaffingOptionDto(
                group.GroupId,
                group.Name,
                group.Description,
                group.MajorId,
                group.MajorName,
                group.MaxMembers,
                group.CurrentMembers,
                group.RemainingSlots,
                suggestions.Count == 0 ? 0 : suggestions.Max(s => s.Score),
                suggestions));
        }

        var remainingStudents = availablePool
            .Select(c => c.Snapshot)
            .ToList();

        var studentOptions = students
            .Select(student =>
            {
                var candidate = candidateStates[student.UserId];
                placements.TryGetValue(student.UserId, out var placement);
                var tags = candidate.Profile.Tags.Take(5).ToList();

                return new StudentPlacementOptionDto(
                    student.UserId,
                    student.DisplayName,
                    student.MajorId,
                    candidate.MajorName,
                    student.Gpa,
                    student.DesiredPositionName,
                    AiRoleHelper.ToDisplayString(candidate.Profile.PrimaryRole),
                    tags,
                    placement?.Score ?? 0,
                    placement?.Reason,
                    placement,
                    placement is null);
            })
            .ToList();

        return (groupOptions, studentOptions, remainingStudents);
    }

    private async Task<NewGroupCreationResult> CreateNewGroupsForStudentsAsync(
        IReadOnlyList<StudentProfileSnapshot> remainingStudents,
        SemesterContext semesterCtx,
        Guid actorUserId,
        Guid? preferredMajorId,
        IReadOnlyDictionary<Guid, string> majorLookup,
        CancellationToken ct)
    {
        if (remainingStudents.Count == 0)
            return NewGroupCreationResult.Empty;

        var minSize = Math.Max(semesterCtx.Policy.DesiredGroupSizeMin, 1);
        var policyMax = semesterCtx.Policy.DesiredGroupSizeMax;
        if (policyMax <= 0)
            policyMax = minSize;
        var maxSize = Math.Max(minSize, policyMax);

        var groups = new List<AutoResolveNewGroupDto>();
        var assignments = new List<AutoAssignmentRecordDto>();
        var topicAssignments = new List<AutoAssignTopicResultDto>();
        var topicFailures = new List<Guid>();
        var unresolved = new List<StudentAssignmentIssueDto>();
        var counter = 1;

        var groupedByMajor = remainingStudents
            .GroupBy(s => s.MajorId)
            .OrderBy(g => g.Key)
            .ToList();

        async Task CreateGroupFromBatchAsync(List<StudentProfileSnapshot> batch, Guid? enforcedMajorId)
        {
            if (batch.Count == 0)
                return;

            var groupName = await GenerateUniqueAutoGroupNameAsync(semesterCtx.SemesterId, counter, ct);
            counter++;
            var majorId = enforcedMajorId ?? DetermineGroupMajor(batch, preferredMajorId);
            var description = $"Nhóm được tạo tự động vào {DateTime.UtcNow:dd/MM/yyyy}.";
            var skillsJson = BuildGroupSkillsJson(batch);
            var maxMembers = Math.Clamp(batch.Count, minSize, maxSize);
            var groupId = await groupRepository.CreateGroupAsync(
                semesterCtx.SemesterId,
                null,
                majorId,
                groupName,
                description,
                maxMembers,
                skillsJson,
                ct);

            for (var i = 0; i < batch.Count; i++)
            {
                var status = i == 0 ? "leader" : "member";
                await groupRepository.AddMembershipAsync(groupId, batch[i].UserId, semesterCtx.SemesterId, status, ct);
                await postRepository.DeleteProfilePostsForUserAsync(batch[i].UserId, semesterCtx.SemesterId, ct);
                assignments.Add(new AutoAssignmentRecordDto(
                    batch[i].UserId,
                    groupId,
                    groupName,
                    AiRoleHelper.ToDisplayString(AiRoleHelper.Parse(batch[i].PrimaryRole))));
            }
            groups.Add(new AutoResolveNewGroupDto(
                groupId,
                groupName,
                majorId,
                GetMajorName(majorId, majorLookup),
                batch.Count,
                null,
                null,
                batch.Select(s => s.UserId).ToList()));
        }

        foreach (var majorGroup in groupedByMajor)
        {
            var ordered = majorGroup
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sizes = ComputeBalancedGroupSizes(ordered.Count, minSize, maxSize);
            if (sizes.Count == 0)
            {
                // Not enough students to form a valid group under policy.
                foreach (var s in ordered)
                {
                    unresolved.Add(new StudentAssignmentIssueDto(
                        s.UserId,
                        $"Không đủ sinh viên để tạo nhóm mới (policy min {minSize}, max {maxSize})."));
                }
                continue;
            }

            var offset = 0;
            foreach (var size in sizes)
            {
                var batch = ordered.Skip(offset).Take(size).ToList();
                offset += size;
                await CreateGroupFromBatchAsync(batch, majorGroup.Key);
            }
        }

        return new NewGroupCreationResult(groups, assignments, topicAssignments, topicFailures, unresolved);
    }

    private static List<int> ComputeBalancedGroupSizes(int total, int minSize, int maxSize)
    {
        if (total <= 0)
            return new List<int>();

        if (total < minSize)
            return new List<int>();

        if (maxSize < minSize)
            maxSize = minSize;

        var minGroups = (int)Math.Ceiling(total / (double)maxSize);
        var maxGroups = total / minSize;
        if (maxGroups < minGroups)
            return new List<int>();

        for (var groupsCount = minGroups; groupsCount <= maxGroups; groupsCount++)
        {
            var baseSize = total / groupsCount;
            var remainder = total % groupsCount;

            // Sizes differ by at most 1: (base+1) repeated remainder times.
            var sizes = new List<int>(groupsCount);
            for (var i = 0; i < groupsCount; i++)
            {
                var size = i < remainder ? baseSize + 1 : baseSize;
                if (size < minSize || size > maxSize)
                {
                    sizes.Clear();
                    break;
                }
                sizes.Add(size);
            }

            if (sizes.Count == groupsCount)
                return sizes;
        }

        return new List<int>();
    }

    private async Task<bool> TryActivateGroupIfReadyAsync(
        Guid groupId,
        int? activeCount,
        int? maxMembers,
        CancellationToken ct)
    {
        var detail = await groupQueries.GetGroupAsync(groupId, ct);
        if (detail is null)
            return false;

        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!detail.TopicId.HasValue)
            return false;

        var mentor = await groupQueries.GetMentorAsync(groupId, ct);
        if (mentor is null)
            return false;

        int max;
        int count;

        if (activeCount.HasValue && maxMembers.HasValue)
        {
            max = maxMembers.Value;
            count = activeCount.Value;
        }
        else
        {
            var cap = await groupQueries.GetGroupCapacityAsync(groupId, ct);
            max = cap.MaxMembers;
            count = cap.ActiveCount;
        }

        if (count < max)
            return false;

        await groupRepository.SetStatusAsync(groupId, "active", ct);
        await postRepository.CloseAllOpenPostsForGroupAsync(groupId, ct);
        return true;
    }

    private static (int Index, int Score) FindBestCandidate(
        List<StudentCandidate> pool,
        GroupOverviewSnapshot group,
        GroupRoleMixSnapshot mix)
    {
        return FindBestCandidate(pool, group, mix, extraFilter: null);
    }

    private static (int Index, int Score) FindBestCandidate(
        List<StudentCandidate> pool,
        GroupOverviewSnapshot group,
        GroupRoleMixSnapshot mix,
        Func<StudentCandidate, bool>? extraFilter)
    {
        var bestIndex = -1;
        var bestScore = int.MinValue;

        for (var i = 0; i < pool.Count; i++)
        {
            var candidate = pool[i];
            if (group.MajorId.HasValue && candidate.Snapshot.MajorId != group.MajorId.Value)
                continue;
            if (extraFilter is not null && !extraFilter(candidate))
                continue;
            var score = ScoreCandidateForGroup(candidate.Profile, candidate.Snapshot.MajorId, group, mix);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return (bestIndex, bestScore);
    }

    private static Dictionary<Guid, double> ComputeHighGpaThresholdByMajor(IReadOnlyList<StudentProfileSnapshot> students)
    {
        // "High GPA" = 75th percentile within the major.
        return students
            .GroupBy(s => s.MajorId)
            .Select(g => new
            {
                MajorId = g.Key,
                Threshold = ComputeHighGpaThreshold(g.Select(s => s.Gpa))
            })
            .Where(x => x.Threshold.HasValue)
            .ToDictionary(x => x.MajorId, x => x.Threshold!.Value);
    }

    private static double? ComputeHighGpaThreshold(IEnumerable<double?> gpas)
    {
        var list = gpas
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x)
            .ToList();

        if (list.Count < 6)
            return null;

        var idx = (int)Math.Floor((list.Count - 1) * 0.75);
        idx = Math.Clamp(idx, 0, list.Count - 1);
        return list[idx];
    }

    private static int ScoreCandidateForGroup(
        AiSkillProfile profile,
        Guid studentMajorId,
        GroupOverviewSnapshot group,
        GroupRoleMixSnapshot mix)
    {
        var roleAdjustment = CalculateRoleNeedAdjustment(profile.PrimaryRole, mix);
        var majorBoost = group.MajorId.HasValue && group.MajorId.Value == studentMajorId ? 15 : 5;
        var skillBoost = profile.Tags.Take(5).Count() * 3;
        return roleAdjustment + majorBoost + skillBoost;
    }

    private static string BuildCandidateReason(
        GroupOverviewSnapshot group,
        GroupRoleMixSnapshot mix,
        AiSkillProfile profile,
        StudentProfileSnapshot snapshot,
        int score)
    {
        var reasons = new List<string>();
        if (group.MajorId.HasValue && group.MajorId.Value == snapshot.MajorId)
            reasons.Add("Cùng chuyên ngành");

        var roleReason = DescribeRoleNeed(profile.PrimaryRole, mix);
        if (!string.IsNullOrWhiteSpace(roleReason))
            reasons.Add(roleReason);

        if (profile.HasTags)
            reasons.Add("Kỹ năng: " + string.Join(", ", profile.Tags.Take(3)));

        // Heuristic baseline only; AI rerank will provide final score/reason.
        return string.Join(". ", reasons);
    }

    private static string BuildGroupIssueReason(
        GroupAssignmentState state,
        RolePools? pools,
        bool limitConfigured,
        bool limitHit,
        int? originalLimit,
        int policyMax)
    {
        var remainingCandidates = pools?.RemainingCount ?? 0;
        var hasRemainingCandidates = remainingCandidates > 0;

        if (limitConfigured && limitHit && hasRemainingCandidates)
        {
            if (originalLimit.HasValue && originalLimit.Value > 0)
                return $"Đã đạt giới hạn {originalLimit.Value} lượt auto assign trong lần chạy này, nhóm vẫn thiếu {state.RemainingSlots} thành viên.";
            return "Đã đạt giới hạn auto assign trong lần chạy này nên chưa lấp đủ thành viên.";
        }

        if (hasRemainingCandidates && state.MaxMembers >= policyMax && policyMax > 0)
            return $"Nhóm đã chạm giới hạn tối đa {policyMax} thành viên theo policy nên không thể nhận thêm thành viên.";

        if (!hasRemainingCandidates)
            return $"Không còn sinh viên chưa có nhóm trong chuyên ngành này. Nhóm vẫn thiếu {state.RemainingSlots} thành viên.";

        if (!state.HasBackend && !(pools?.HasBackendCandidates ?? false))
            return "Không còn sinh viên Backend phù hợp để bổ sung cho nhóm.";
        if (!state.HasFrontend && !(pools?.HasFrontendCandidates ?? false))
            return "Không còn sinh viên Frontend phù hợp để bổ sung cho nhóm.";
        if (!(pools?.HasOtherCandidates ?? false))
            return "Không còn sinh viên phù hợp với vai trò hỗ trợ/khác để bổ sung cho nhóm.";

        return "Các sinh viên còn lại không đáp ứng vai trò mà nhóm đang cần.";
    }

    private static string? DescribeRoleNeed(AiPrimaryRole role, GroupRoleMixSnapshot mix)
    {
        var frontendHeavy = mix.FrontendCount >= 2
                            && mix.FrontendCount >= mix.BackendCount + 1
                            && mix.FrontendCount >= mix.OtherCount + 1;

        var backendNeeded = mix.BackendCount == 0
                            || (mix.FrontendCount >= 2 && mix.BackendCount + 1 <= mix.FrontendCount - 1);
        var mobileNeeded = mix.OtherCount == 0 && mix.FrontendCount >= 2;

        if (backendNeeded && role == AiPrimaryRole.Backend)
            return "Nhóm đang thiếu Backend";
        if (mix.FrontendCount == 0 && role == AiPrimaryRole.Frontend)
            return "Nhóm đang thiếu Frontend";
        if (mobileNeeded && role == AiPrimaryRole.Other)
            return "Cần thêm Mobile/Generalist";
        if (frontendHeavy && role == AiPrimaryRole.Backend)
            return "Cân bằng Frontend/Backend";
        return null;
    }

    private static GroupRoleMixSnapshot ApplyRoleToMix(GroupRoleMixSnapshot mix, AiPrimaryRole role)
        => role switch
        {
            AiPrimaryRole.Frontend => mix with { FrontendCount = mix.FrontendCount + 1 },
            AiPrimaryRole.Backend => mix with { BackendCount = mix.BackendCount + 1 },
            AiPrimaryRole.Other => mix with { OtherCount = mix.OtherCount + 1 },
            _ => mix
        };

    private static string BuildTopicReason(
        TopicAvailabilitySnapshot topic,
        TopicSuggestionDto suggestion,
        GroupOverviewSnapshot group)
    {
        var reasons = new List<string>();
        if (group.MajorId.HasValue && topic.MajorId.HasValue && group.MajorId.Value == topic.MajorId.Value)
            reasons.Add("Cùng chuyên ngành");
        if (suggestion.MatchingSkills is { Count: > 0 })
            reasons.Add("Khớp kỹ năng: " + string.Join(", ", suggestion.MatchingSkills));
        if (topic.CanTakeMore)
            reasons.Add("Topic còn trống");
        if (reasons.Count == 0)
            reasons.Add("Điểm AI " + suggestion.Score + "%");
        return string.Join(". ", reasons);
    }

    private static PaginatedCollection<T> Paginate<T>(IReadOnlyList<T> source, int page, int pageSize)
    {
        var total = source.Count;
        if (total == 0)
            return new PaginatedCollection<T>(0, page, pageSize, Array.Empty<T>());

        var skip = (page - 1) * pageSize;
        var items = source.Skip(skip).Take(pageSize).ToList();
        return new PaginatedCollection<T>(total, page, pageSize, items);
    }

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize)
    {
        var normalizedPage = page <= 0 ? 1 : page;
        var normalizedSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 50);
        return (normalizedPage, normalizedSize);
    }

    private static string? GetMajorName(Guid? majorId, IReadOnlyDictionary<Guid, string> lookup)
    {
        if (!majorId.HasValue)
            return null;
        return lookup.TryGetValue(majorId.Value, out var name) ? name : null;
    }

    private static Guid? DetermineGroupMajor(
        IReadOnlyCollection<StudentProfileSnapshot> batch,
        Guid? preferredMajorId)
    {
        if (preferredMajorId.HasValue)
            return preferredMajorId;

        return batch
            .GroupBy(s => s.MajorId)
            .OrderByDescending(g => g.Count())
            .Select(g => (Guid?)g.Key)
            .FirstOrDefault();
    }

    private Task<string> GenerateUniqueAutoGroupNameAsync(Guid semesterId, int seed, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var name = $"AI Auto Group {seed:00}-{suffix}";
        return Task.FromResult(name.Length > 64 ? name[..64] : name);
    }

    private sealed record StudentCandidate(
        StudentProfileSnapshot Snapshot,
        AiSkillProfile Profile,
        string? MajorName);

    private sealed record StudentAssignmentPhaseResult(
        IReadOnlyList<AutoAssignmentRecordDto> Assignments,
        IReadOnlyList<StudentProfileSnapshot> RemainingStudents,
        IReadOnlyList<Guid> OpenGroupIds,
        IReadOnlyList<GroupAssignmentIssueDto> GroupIssues);

    private sealed record TopicAssignmentPhaseResult(
        IReadOnlyList<AutoAssignTopicResultDto> Assignments,
        IReadOnlyList<Guid> SkippedGroupIds,
        IReadOnlyList<TopicAssignmentIssueDto> Issues);

    private sealed record TopicAssignmentAttemptResult(
        AutoAssignTopicResultDto? Assignment,
        string? FailureReason)
    {
        public bool HasAssignment => Assignment is not null;

        public static TopicAssignmentAttemptResult FromSuccess(AutoAssignTopicResultDto result)
            => new(result, null);

        public static TopicAssignmentAttemptResult Fail(string reason)
            => new(null, reason);
    }

    private sealed record NewGroupCreationResult(
        IReadOnlyList<AutoResolveNewGroupDto> Groups,
        IReadOnlyList<AutoAssignmentRecordDto> Assignments,
        IReadOnlyList<AutoAssignTopicResultDto> TopicAssignments,
        IReadOnlyList<Guid> TopicFailures,
        IReadOnlyList<StudentAssignmentIssueDto> UnresolvedStudents)
    {
        public static NewGroupCreationResult Empty { get; } = new(
            Array.Empty<AutoResolveNewGroupDto>(),
            Array.Empty<AutoAssignmentRecordDto>(),
            Array.Empty<AutoAssignTopicResultDto>(),
            Array.Empty<Guid>(),
            Array.Empty<StudentAssignmentIssueDto>());
    }

    private async Task<TopicAssignmentAttemptResult> AssignTopicToGroupAsync(
        Guid groupId,
        Guid actorUserId,
        bool enforceMembership,
        int? limit,
        bool throwIfUnavailable,
        CancellationToken ct)
    {
        var detail = await groupQueries.GetGroupAsync(groupId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm.");

        if (detail.TopicId.HasValue)
            return FailTopicAssignment("Nhóm đã có topic.", throwIfUnavailable);

        var suggestions = await SuggestTopicsInternalAsync(groupId, actorUserId, enforceMembership, limit, ct);
        if (suggestions.Count == 0)
            return FailTopicAssignment("Không tìm thấy topic phù hợp.", throwIfUnavailable);

        var chosen = suggestions.First();
        var mentorId = await topicQueries.GetDefaultMentorIdAsync(chosen.TopicId, ct);
        if (!mentorId.HasValue)
        {
            var topicDetail = await topicQueries.GetByIdAsync(chosen.TopicId, ct);
            mentorId = topicDetail?.Mentors.FirstOrDefault()?.MentorId;
        }

        if (!mentorId.HasValue)
            return FailTopicAssignment("Topic chưa cấu hình mentor.", throwIfUnavailable);

        await groupRepository.UpdateGroupAsync(groupId, null, null, null, null, chosen.TopicId, mentorId, null, ct);
        await topicWriteRepository.SetStatusAsync(chosen.TopicId, "closed", ct);

        // Only activate when the group is full AND already has topic+mentor.
        await TryActivateGroupIfReadyAsync(groupId, null, null, ct);

        return TopicAssignmentAttemptResult.FromSuccess(new AutoAssignTopicResultDto(groupId, chosen.TopicId, chosen.Title, chosen.Score));
    }

    private static TopicAssignmentAttemptResult FailTopicAssignment(string message, bool throwIfUnavailable)
    {
        if (throwIfUnavailable)
            throw new InvalidOperationException(message);
        return TopicAssignmentAttemptResult.Fail(message);
    }

    private async Task<IReadOnlyList<RecruitmentPostSuggestionDto>> HydrateRecruitmentPostSuggestionsAsync(
        IReadOnlyList<RecruitmentPostSuggestionDto> suggestions,
        Guid currentUserId,
        CancellationToken ct)
    {
        if (suggestions.Count == 0)
            return suggestions;

        var expand = ExpandOptions.Semester | ExpandOptions.Group | ExpandOptions.Major;
        var enriched = new List<RecruitmentPostSuggestionDto>(suggestions.Count);

        foreach (var suggestion in suggestions)
        {
            var detail = await recruitmentPostQueries.GetAsync(suggestion.PostId, expand, currentUserId, ct);
            enriched.Add(detail is null ? suggestion : suggestion with { Detail = detail });
        }

        return enriched;
    }

    private async Task<IReadOnlyList<ProfilePostSuggestionDto>> HydrateProfilePostSuggestionsAsync(
        IReadOnlyList<ProfilePostSuggestionDto> suggestions,
        Guid currentUserId,
        CancellationToken ct)
    {
        if (suggestions.Count == 0)
            return suggestions;

        var expand = ExpandOptions.Semester | ExpandOptions.Major | ExpandOptions.User;
        var enriched = new List<ProfilePostSuggestionDto>(suggestions.Count);

        foreach (var suggestion in suggestions)
        {
            var detail = await recruitmentPostQueries.GetProfilePostAsync(suggestion.PostId, expand, currentUserId, ct);
            enriched.Add(detail is null ? suggestion : suggestion with { Detail = detail });
        }

        return enriched;
    }

    private async Task<IReadOnlyList<TopicSuggestionDto>> HydrateTopicSuggestionsAsync(
        IReadOnlyList<TopicSuggestionDto> suggestions,
        CancellationToken ct)
    {
        if (suggestions.Count == 0)
            return suggestions;

        var enriched = new List<TopicSuggestionDto>(suggestions.Count);

        foreach (var suggestion in suggestions)
        {
            var detail = await topicQueries.GetByIdAsync(suggestion.TopicId, ct);
            enriched.Add(detail is null ? suggestion : suggestion with { Detail = detail });
        }

        return enriched;
    }

    private static string? BuildGroupSkillsJson(IReadOnlyCollection<StudentProfileSnapshot> batch)
    {
        if (batch.Count == 0)
            return null;

        var profiles = batch
            .Select(BuildSkillProfile)
            .Where(p => p.HasTags || p.PrimaryRole != AiPrimaryRole.Unknown)
            .ToList();

        if (profiles.Count == 0)
            return null;

        var combined = AiSkillProfile.Combine(profiles);
        if (!combined.HasTags && combined.PrimaryRole == AiPrimaryRole.Unknown)
            return null;

        var payload = new
        {
            primaryRole = AiRoleHelper.ToDisplayString(combined.PrimaryRole),
            skillTags = combined.Tags
        };

        return JsonSerializer.Serialize(payload);
    }

    private static List<AiSkillProfile> BuildMemberSkillProfiles(IEnumerable<GroupMemberSkillSnapshot> members)
    {
        return members
            .Select(m => AiSkillProfile.FromJson(m.SkillsJson))
            .Where(p => p.HasTags || p.PrimaryRole != AiPrimaryRole.Unknown)
            .ToList();
    }

    private static AiSkillProfile BuildGroupSkillProfile(IReadOnlyList<string>? groupSkills, IReadOnlyCollection<AiSkillProfile> memberProfiles)
    {
        var sources = new List<AiSkillProfile>();

        if (memberProfiles.Count > 0)
            sources.AddRange(memberProfiles);

        if (groupSkills is { Count: > 0 })
        {
            var canonical = groupSkills
                .Where(skill => !string.IsNullOrWhiteSpace(skill))
                .Select(skill => skill.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (canonical.Count > 0)
            {
                var inferredRole = AiRoleHelper.InferFromTags(canonical);
                sources.Add(new AiSkillProfile(inferredRole, canonical));
            }
        }

        return sources.Count == 0 ? AiSkillProfile.Empty : AiSkillProfile.Combine(sources);
    }

    private static string BuildSemanticQuery(AiSkillProfile profile)
    {
        var parts = new List<string>();
        if (profile.PrimaryRole != AiPrimaryRole.Unknown)
            parts.Add(AiRoleHelper.ToDisplayString(profile.PrimaryRole));
        if (profile.HasTags)
            parts.Add(string.Join(", ", profile.Tags));
        return string.Join("; ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildSemanticQuery(AiSkillProfile primary, AiSkillProfile secondary)
    {
        var primaryText = BuildSemanticQuery(primary);
        var secondaryText = BuildSemanticQuery(secondary);
        if (string.IsNullOrWhiteSpace(primaryText))
            return secondaryText;
        if (string.IsNullOrWhiteSpace(secondaryText))
            return primaryText;
        return primaryText + "; " + secondaryText;
    }

    private static string BuildStudentQueryText(StudentProfileSnapshot student, AiSkillProfile profile, Guid? targetMajorId)
    {
        var builder = new StringBuilder();
        builder.Append(student.DisplayName);
        builder.Append(" - Major: ").Append(targetMajorId?.ToString() ?? student.MajorId.ToString());

        if (!string.IsNullOrWhiteSpace(student.DesiredPositionName))
            builder.Append(" - Desired position: ").Append(student.DesiredPositionName.Trim());

        if (profile.PrimaryRole != AiPrimaryRole.Unknown)
            builder.Append(" - Role: ").Append(AiRoleHelper.ToDisplayString(profile.PrimaryRole));

        if (profile.HasTags)
            builder.Append(" - Skills: ").Append(string.Join(", ", profile.Tags.Take(15)));

        return builder.ToString();
    }


    private static string BuildGroupQueryText(string groupName, AiSkillProfile profile, GroupRoleMixSnapshot? mix)
    {
        var builder = new StringBuilder();
        builder.Append(groupName);
        if (profile.PrimaryRole != AiPrimaryRole.Unknown)
            builder.Append(" - Primary need: ").Append(AiRoleHelper.ToDisplayString(profile.PrimaryRole));
        if (profile.HasTags)
            builder.Append(" - Skills: ").Append(string.Join(", ", profile.Tags.Take(15)));

        if (mix is not null)
        {
            builder.Append(" - Current mix: ")
                .Append($"FE {mix.FrontendCount}, BE {mix.BackendCount}, Other {mix.OtherCount}");
        }

        return builder.ToString();
    }

    private static string BuildGroupQueryText(
        string groupName,
        AiSkillProfile needsProfile,
        AiSkillProfile groupProfile,
        IReadOnlyList<RecruitmentPostSnapshot> recruitmentPosts,
        GroupRoleMixSnapshot? mix)
    {
        var builder = new StringBuilder();
        builder.Append(groupName);

        // If there are no recruitment posts, still describe role needs via the role mix,
        // so the AI can compare profile posts against the group's missing roles.
        if (recruitmentPosts.Count > 0 || (needsProfile.PrimaryRole != AiPrimaryRole.Unknown || needsProfile.HasTags))
        {
            if (needsProfile.PrimaryRole != AiPrimaryRole.Unknown)
                builder.Append(" - Hiring need: ").Append(AiRoleHelper.ToDisplayString(needsProfile.PrimaryRole));
            if (needsProfile.HasTags)
                builder.Append(" - Required skills: ").Append(string.Join(", ", needsProfile.Tags.Take(15)));
        }

        if (groupProfile.PrimaryRole != AiPrimaryRole.Unknown)
            builder.Append(" - Team role: ").Append(AiRoleHelper.ToDisplayString(groupProfile.PrimaryRole));
        if (groupProfile.HasTags)
            builder.Append(" - Team skills: ").Append(string.Join(", ", groupProfile.Tags.Take(15)));

        if (mix is not null)
        {
            builder.Append(" - Current mix: ")
                .Append($"FE {mix.FrontendCount}, BE {mix.BackendCount}, Other {mix.OtherCount}");
        }

        return builder.ToString();
    }

    private static AiLlmCandidate BuildRecruitmentCandidate(RecruitmentPostSuggestionDto suggestion)
    {
        var baselineSkills = suggestion.RequiredSkills ?? Array.Empty<string>();

        // IMPORTANT: recruitment-post descriptions are user-generated and not controlled.
        // For rerank/reasons, only send the controlled signals: required skills + position needed.
        var controlledSummary = string.IsNullOrWhiteSpace(suggestion.PositionNeeded)
            ? "Recruiting."
            : $"Recruiting for {suggestion.PositionNeeded}.";

        var payload = BuildStructuredCandidateText(
            suggestion.Title,
            controlledSummary,
            baselineSkills,
            suggestion.PositionNeeded,
            null,
            suggestion.MatchingSkills);

        var metadata = BuildMetadata(
            ("majorId", suggestion.MajorId?.ToString()),
            ("groupId", suggestion.GroupId?.ToString()),
            ("score", suggestion.Score.ToString(CultureInfo.InvariantCulture)),
            ("position", suggestion.PositionNeeded),
            ("neededRole", suggestion.PositionNeeded));

        return new AiLlmCandidate(
            string.Empty,
            suggestion.PostId,
            suggestion.Title,
            null,
            payload,
            metadata);
    }

    private static AiLlmCandidate BuildTopicCandidate(TopicSuggestionDto suggestion)
    {
        var payload = BuildStructuredCandidateText(
            suggestion.Title,
            suggestion.Description,
            suggestion.TopicSkills,
            null,
            suggestion.CanTakeMore,
            suggestion.MatchingSkills);

        var metadata = BuildMetadata(
            ("score", suggestion.Score.ToString(CultureInfo.InvariantCulture)));

        return new AiLlmCandidate(
            string.Empty,
            suggestion.TopicId,
            suggestion.Title,
            suggestion.Description,
            payload,
            metadata);
    }

    private static AiLlmCandidate BuildProfileCandidate(ProfilePostSuggestionDto suggestion)
    {
        var profileSkills = SplitSkillTokens(suggestion.SkillsText).Concat(suggestion.MatchingSkills);
        var description = string.IsNullOrWhiteSpace(suggestion.Description)
            ? suggestion.SkillsText
            : suggestion.Description;

        var payload = BuildStructuredCandidateText(
            suggestion.Title,
            description,
            profileSkills,
            suggestion.PrimaryRole,
            null,
            suggestion.MatchingSkills);

        var metadata = BuildMetadata(
            ("majorId", suggestion.MajorId?.ToString()),
            ("score", suggestion.Score.ToString(CultureInfo.InvariantCulture)),
            ("ownerUserId", suggestion.OwnerUserId.ToString()),
            ("neededRole", suggestion.PrimaryRole));

        return new AiLlmCandidate(
            string.Empty,
            suggestion.PostId,
            suggestion.Title,
            suggestion.Description,
            payload,
            metadata);
    }
    private static string? BuildGpa10Line(double? gpaOn4)
    {
        if (!gpaOn4.HasValue)
            return null;

        var gpa4 = Math.Clamp(gpaOn4.Value, 0, 4);
        var gpa10 = gpa4 * 2.5;
        return $"GPA: {gpa10.ToString("0.00", CultureInfo.InvariantCulture)}/10.";
    }

    private static string BuildStructuredCandidateText(
        string title,
        string? description,
        IEnumerable<string>? skills,
        string? roleNeeded,
        bool? canTakeMore,
        IEnumerable<string>? matchingHighlights,
        IEnumerable<string?>? extraLines = null)
    {
        var normalizedSkills = NormalizeSkillTokens(skills).Take(35).ToList();
        var normalizedMatches = NormalizeSkillTokens(matchingHighlights).Take(15).ToList();

        var summary = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (!string.IsNullOrWhiteSpace(summary) && summary.Length > 1200)
            summary = summary[..1200];

        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
            summaryParts.Add(title.Trim());
        if (!string.IsNullOrWhiteSpace(roleNeeded))
            summaryParts.Add($"Role: {roleNeeded.Trim()}");
        if (canTakeMore.HasValue)
            summaryParts.Add(canTakeMore.Value ? "Slots available" : "At capacity");
        if (!string.IsNullOrWhiteSpace(summary))
            summaryParts.Add(summary);
        var combinedSummary = string.Join(" - ", summaryParts.Where(part => !string.IsNullOrWhiteSpace(part)));

        var builder = new StringBuilder();
        builder.AppendLine("SKILLS: " + (normalizedSkills.Count == 0 ? "n/a" : string.Join(", ", normalizedSkills)));
        builder.AppendLine("MATCHING_SKILLS: " + (normalizedMatches.Count == 0 ? "n/a" : string.Join(", ", normalizedMatches)));
        if (extraLines is not null)
        {
            foreach (var line in extraLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    builder.AppendLine(line.Trim());
            }
        }
        builder.Append("SUMMARY: ").Append(combinedSummary);
        return builder.ToString();
    }

    private static string BuildGroupPostTeamContext(
        string groupName,
        int openSlots,
        GroupRoleMixSnapshot mix,
        AiSkillProfile profile,
        AiSkillProfile needsProfile,
        string? needsText)
    {
        var primaryNeed = InferPrimaryNeedFromMix(mix);
        var prefer = string.IsNullOrWhiteSpace(primaryNeed)
            ? Array.Empty<string>()
            : (primaryNeed is "frontend" or "backend"
                ? new[] { primaryNeed, "fullstack" }
                : new[] { primaryNeed });

        var payload = new
        {
            name = groupName,
            requirements = string.IsNullOrWhiteSpace(needsText)
                ? null
                : new
                {
                    text = needsText,
                    requiredSkills = needsProfile.Tags.Take(15).ToList(),
                    requiredRole = needsProfile.PrimaryRole == AiPrimaryRole.Unknown
                        ? null
                        : AiRoleHelper.ToDisplayString(needsProfile.PrimaryRole)
                },
            primaryNeed,
            openSlots,
            mix = new
            {
                fe = mix.FrontendCount,
                be = mix.BackendCount,
                ai = 0,
                other = mix.OtherCount
            },
            preferRoles = prefer,
            avoidRoles = Array.Empty<string>(),
            teamTopSkills = profile.Tags.Take(15).ToList()
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string? InferPrimaryNeedFromMix(GroupRoleMixSnapshot mix)
    {
        if (mix.FrontendCount == 0)
            return "frontend";
        if (mix.BackendCount == 0)
            return "backend";
        if (mix.OtherCount == 0)
            return "other";
        return null;
    }

    private static IEnumerable<string> NormalizeSkillTokens(IEnumerable<string>? source)
    {
        if (source is null)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in source)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var normalized = token.Trim();
            if (normalized.Length == 0 || !seen.Add(normalized))
                continue;

            yield return normalized;
        }
    }

    private static IEnumerable<string> SplitSkillTokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        var parts = raw.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static (RecruitmentPostSuggestionDto Updated, double? OverrideScore) ApplyRecruitmentRerank(
        RecruitmentPostSuggestionDto suggestion,
        AiLlmRerankedItem? reranked)
    {
        if (reranked is null)
            return (suggestion, null);

        // IMPORTANT: recruitment-post matching skills are deterministic overlap between
        // student skills and post requirements. Do not allow the LLM to overwrite them.
        var matches = suggestion.MatchingSkills;
        var chosenReason = ChooseAiReason(
            llmReason: reranked.Reason,
            existingReason: suggestion.AiReason,
            title: suggestion.Title,
            description: suggestion.Description,
            fallback: BuildRecruitmentFallbackReason(suggestion));
        var updated = suggestion with
        {
            // Use rerank finalScore as the displayed score (0-100) as requested.
            Score = NormalizeLlmScore(reranked.FinalScore),
            AiReason = chosenReason,
            AiBalanceNote = reranked.BalanceNote ?? suggestion.AiBalanceNote,
            MatchingSkills = matches
        };

        return (updated, reranked.FinalScore);
    }

    private static (TopicSuggestionDto Updated, double? OverrideScore) ApplyTopicRerank(
        TopicSuggestionDto suggestion,
        AiLlmRerankedItem? reranked)
    {
        if (reranked is null)
            return (suggestion, null);

        var matches = ChooseMatches(suggestion.MatchingSkills, reranked.MatchedSkills);
        var chosenReason = ChooseAiReason(
            llmReason: reranked.Reason,
            existingReason: suggestion.AiReason,
            title: suggestion.Title,
            description: suggestion.Description,
            fallback: BuildTopicFallbackReason(suggestion, matches));
        var updated = suggestion with
        {
            Score = NormalizeLlmScore(reranked.FinalScore),
            AiReason = chosenReason,
            AiBalanceNote = reranked.BalanceNote ?? suggestion.AiBalanceNote,
            MatchingSkills = matches
        };

        return (updated, reranked.FinalScore);
    }

    private static (ProfilePostSuggestionDto Updated, double? OverrideScore) ApplyProfileRerank(
        ProfilePostSuggestionDto suggestion,
        AiLlmRerankedItem? reranked)
    {
        if (reranked is null)
            return (suggestion, null);

        var matches = ChooseMatches(suggestion.MatchingSkills, reranked.MatchedSkills);
        var chosenReason = ChooseAiReason(
            llmReason: reranked.Reason,
            existingReason: suggestion.AiReason,
            title: suggestion.Title,
            description: suggestion.Description,
            fallback: BuildProfilePostFallbackReason(suggestion, matches));
        var updated = suggestion with
        {
            Score = NormalizeLlmScore(reranked.FinalScore),
            AiReason = chosenReason,
            AiBalanceNote = reranked.BalanceNote ?? suggestion.AiBalanceNote,
            MatchingSkills = matches
        };

        return (updated, reranked.FinalScore);
    }

    private async Task<List<TSuggestion>> ApplyLlmRerankAsync<TSuggestion>(
        string queryType,
        string queryText,
        IReadOnlyList<TSuggestion> suggestions,
        Func<TSuggestion, Guid> idSelector,
        Func<TSuggestion, AiLlmCandidate> candidateFactory,
        Func<TSuggestion, AiLlmRerankedItem?, (TSuggestion Updated, double? OverrideScore)> projector,
        Func<TSuggestion, double> fallbackScoreSelector,
        IReadOnlyDictionary<string, string>? context,
        bool requireAiReason,
        CancellationToken ct)
    {
        var suggestionList = suggestions.ToList();
        if (suggestionList.Count == 0 || string.IsNullOrWhiteSpace(queryText))
            return suggestionList;

        var rerankPool = suggestionList.Take(LlmCandidatePoolSize).ToList();
        if (rerankPool.Count == 0)
            return suggestionList;

        var candidates = rerankPool
            .Select(candidateFactory)
            .ToList();

        if (candidates.Count == 0)
            return suggestionList;

        for (var i = 0; i < candidates.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(candidates[i].Key))
                candidates[i] = candidates[i] with { Key = $"c{i + 1:00}" };
        }

        var candidateIdToKey = new Dictionary<Guid, string>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
            candidateIdToKey[candidates[i].Id] = candidates[i].Key;

        if (_llmClient is null)
            return suggestionList;

        AiLlmRerankResponse response;
        try
        {
            var request = new AiLlmRerankRequest(queryType, queryText, candidates, context);
            response = await _llmClient.RerankAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // If the client cancels (e.g., user clicks quickly and the UI aborts the previous call),
            // do not return heuristic reasons as a "successful" response.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM rerank failed for {QueryType}. Using heuristic scores.", queryType);
            return suggestionList;
        }

        var rankedItems = response.Items ?? Array.Empty<AiLlmRerankedItem>();
        if (rankedItems.Count == 0)
        {
            _logger.LogDebug("LLM rerank returned no items for {QueryType}. Using heuristic scores.", queryType);
            return suggestionList;
        }

        var lookup = rankedItems
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hasMatches = false;

        var updatedList = suggestionList
            .Select(suggestion =>
            {
                AiLlmRerankedItem? reranked = null;
                var id = idSelector(suggestion);
                if (candidateIdToKey.TryGetValue(id, out var key) && key is not null && lookup.TryGetValue(key, out var rr))
                {
                    reranked = rr;
                    hasMatches = true;
                }

                var (updated, overrideScore) = projector(suggestion, reranked);
                var rankScore = overrideScore ?? reranked?.FinalScore ?? fallbackScoreSelector(updated);
                if (double.IsNaN(rankScore) || double.IsInfinity(rankScore))
                    rankScore = fallbackScoreSelector(updated);

                return new LlmRankResult<TSuggestion>(updated, rankScore);
            })
            .ToList();

        if (!hasMatches)
            return suggestionList;

        if (requireAiReason)
        {
            // Only keep items that the gateway returned (so the batch is comparable).
            // Do not drop items just because reason text is missing/low-quality.
            updatedList = updatedList
                .Where(item =>
                {
                    var id = idSelector(item.Item);
                    return candidateIdToKey.TryGetValue(id, out var key)
                           && key is not null
                           && lookup.ContainsKey(key);
                })
                .ToList();
        }

        return updatedList
            .OrderByDescending(item => item.Score)
            .Select(item => item.Item)
            .ToList();
    }

    private async Task<(PaginatedCollection<GroupStaffingOptionDto>? Staffing, PaginatedCollection<StudentPlacementOptionDto>? Students)>
        EnrichStaffingOptionReasonsWithAiAsync(
            PaginatedCollection<GroupStaffingOptionDto>? staffingPage,
            PaginatedCollection<StudentPlacementOptionDto>? studentPage,
            CancellationToken ct)
    {
        if (staffingPage is null || staffingPage.Items.Count == 0)
            return (staffingPage, studentPage);

        var updatedGroups = new List<GroupStaffingOptionDto>(staffingPage.Items.Count);
        var studentReasonMap = new Dictionary<Guid, string>();
        var studentScoreMap = new Dictionary<Guid, int>();

        IReadOnlyDictionary<Guid, GroupRoleMixSnapshot> mixByGroupId = new Dictionary<Guid, GroupRoleMixSnapshot>();
        try
        {
            var ids = staffingPage.Items.Select(g => g.GroupId).Distinct().ToArray();
            if (ids.Length > 0)
                mixByGroupId = await aiQueries.GetGroupRoleMixAsync(ids, ct);
        }
        catch
        {
            mixByGroupId = new Dictionary<Guid, GroupRoleMixSnapshot>();
        }

        foreach (var group in staffingPage.Items)
        {
            if (group.SuggestedMembers.Count == 0)
            {
                updatedGroups.Add(group);
                continue;
            }

            var mix = mixByGroupId.TryGetValue(group.GroupId, out var m)
                ? m
                : new GroupRoleMixSnapshot(group.GroupId, 0, 0, 0);

            var primaryNeed = InferPrimaryNeedFromMix(mix);

            var teamContextJson = JsonSerializer.Serialize(new
            {
                teamName = group.Name,
                primaryNeed,
                currentMix = new { fe = mix.FrontendCount, be = mix.BackendCount, other = mix.OtherCount },
                openSlots = new { fe = 0, be = 0, other = Math.Max(group.RemainingSlots, 0) },
                teamTopSkills = Array.Empty<string>()
            });

            var queryText = BuildStructuredCandidateText(
                title: group.Name,
                description: group.Description,
                skills: null,
                roleNeeded: primaryNeed,
                canTakeMore: null,
                matchingHighlights: new[]
                {
                    $"RemainingSlots: {group.RemainingSlots}",
                    $"CurrentMix: FE={mix.FrontendCount}, BE={mix.BackendCount}, Other={mix.OtherCount}"
                });

            var candidates = group.SuggestedMembers
                .Select(m => new AiLlmCandidate(
                    string.Empty,
                    m.StudentId,
                    m.DisplayName,
                    null,
                    BuildStructuredCandidateText(
                        m.DisplayName,
                        $"Role: {m.PrimaryRole ?? "n/a"}. Skills: {(m.SkillTags.Count == 0 ? "n/a" : string.Join(", ", m.SkillTags))}.",
                        m.SkillTags,
                        m.PrimaryRole,
                        null,
                        null,
                        extraLines: new[]
                        {
                            BuildGpa10Line(m.Gpa),
                            string.IsNullOrWhiteSpace(m.DesiredPositionName)
                                ? null
                                : $"Desired position: {m.DesiredPositionName}."
                        }),
                    Metadata: BuildMetadata(
                        ("groupId", group.GroupId.ToString()),
                        // AiLlmClient maps metadata.score -> baselineScore for gateway.
                        ("score", m.Score.ToString(CultureInfo.InvariantCulture)),
                        ("majorId", m.MajorId.ToString()))))
                .ToList();

            for (var i = 0; i < candidates.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(candidates[i].Key))
                    candidates[i] = candidates[i] with { Key = $"c{i + 1:00}" };
            }

            AiLlmRerankResponse response;
            try
            {
                response = await _llmClient.RerankAsync(
                    new AiLlmRerankRequest(
                        "group_staffing_options",
                        queryText,
                        candidates,
                        BuildLlmContext(
                            ("mode", "auto_assign_team"),
                            ("withReasons", "true"),
                            ("team", teamContextJson),
                            ("topN", candidates.Count.ToString(CultureInfo.InvariantCulture)),
                            ("groupId", group.GroupId.ToString()))),
                    ct);
            }
            catch
            {
                updatedGroups.Add(group);
                continue;
            }

            var rrItems = response.Items ?? Array.Empty<AiLlmRerankedItem>();
            var rrByKey = rrItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var keyByStudentId = candidates.ToDictionary(c => c.Id, c => c.Key);

            var updatedMembers = group.SuggestedMembers
                .Select(m =>
                {
                    if (keyByStudentId.TryGetValue(m.StudentId, out var key)
                        && key is not null
                        && rrByKey.TryGetValue(key, out var rrItem)
                        && !string.IsNullOrWhiteSpace(rrItem.Reason))
                    {
                        var normalizedScore = NormalizeLlmScore(rrItem.FinalScore);
                        studentReasonMap[m.StudentId] = rrItem.Reason!;
                        studentScoreMap[m.StudentId] = normalizedScore;
                        return m with
                        {
                            Score = normalizedScore,
                            Reason = rrItem.Reason!
                        };
                    }

                    return m;
                })
                .ToList();

            var groupScore = updatedMembers.Count == 0 ? 0 : updatedMembers.Max(x => x.Score);
            updatedGroups.Add(group with { Score = groupScore, SuggestedMembers = updatedMembers });
        }

        staffingPage = staffingPage with { Items = updatedGroups };

        if (studentPage is not null && studentPage.Items.Count > 0)
        {
            var updatedStudents = studentPage.Items
                .Select(s =>
                {
                    if (s.SuggestedGroup is null)
                        return s;

                    studentReasonMap.TryGetValue(s.StudentId, out var reason);
                    studentScoreMap.TryGetValue(s.StudentId, out var score);

                    if (string.IsNullOrWhiteSpace(reason) && score <= 0)
                        return s;

                    var updatedGroup = s.SuggestedGroup with
                    {
                        Score = score > 0 ? score : s.SuggestedGroup.Score,
                        Reason = !string.IsNullOrWhiteSpace(reason) ? reason : s.SuggestedGroup.Reason
                    };

                    return s with
                    {
                        Score = score > 0 ? score : s.Score,
                        Reason = !string.IsNullOrWhiteSpace(reason) ? reason : s.Reason,
                        SuggestedGroup = updatedGroup
                    };
                })
                .ToList();

            studentPage = studentPage with { Items = updatedStudents };
        }

        return (staffingPage, studentPage);
    }

    private static IReadOnlyList<string> ChooseMatches(IReadOnlyList<string> fallback, IReadOnlyList<string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return fallback;

        var canonical = overrides
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return canonical.Count == 0 ? fallback : canonical;
    }

    private static string? ChooseAiReason(
        string? llmReason,
        string? existingReason,
        string? title,
        string? description,
        string? fallback)
    {
        if (!IsBadAiReason(llmReason, title, description))
            return llmReason;

        if (!string.IsNullOrWhiteSpace(existingReason))
            return existingReason;

        return fallback;
    }

    private static bool IsBadAiReason(string? reason, string? title, string? description)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return true;

        var r = reason.Trim();

        // Common fallback format from snippet builders: "Title | Role: ... | ...".
        if (r.Contains(" | ", StringComparison.Ordinal))
        {
            if (r.Contains("Role:", StringComparison.OrdinalIgnoreCase)
                || r.Contains("Major:", StringComparison.OrdinalIgnoreCase)
                || r.Contains("NeededRole:", StringComparison.OrdinalIgnoreCase)
                || r.Contains("GroupFrontend", StringComparison.OrdinalIgnoreCase)
                || r.Contains("GroupBackend", StringComparison.OrdinalIgnoreCase)
                || r.Contains("GroupOther", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            var d = description.Trim();
            if (d.Length > 0)
            {
                if (string.Equals(r, d, StringComparison.OrdinalIgnoreCase))
                    return true;

                // If it contains the entire description verbatim, it's not a "reason".
                if (d.Length >= 40 && r.Contains(d, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var t = title.Trim();
            if (t.Length > 0 && r.StartsWith(t, StringComparison.OrdinalIgnoreCase) && r.Contains(" | ", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string BuildProfilePostFallbackReason(ProfilePostSuggestionDto suggestion, IReadOnlyList<string> matches)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(suggestion.PrimaryRole))
            parts.Add($"Vai trò phù hợp: {suggestion.PrimaryRole}.");

        var matchList = matches.Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList();
        if (matchList.Count > 0)
            parts.Add("Kỹ năng trùng: " + string.Join(", ", matchList) + ".");

        return parts.Count == 0 ? "Phù hợp với nhu cầu hiện tại của nhóm." : string.Join(" ", parts);
    }

    private static string BuildRecruitmentFallbackReason(RecruitmentPostSuggestionDto suggestion)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(suggestion.PositionNeeded))
            parts.Add($"Nhóm đang cần vị trí: {suggestion.PositionNeeded}.");

        var ms = suggestion.MatchingSkills.Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList();
        if (ms.Count > 0)
            parts.Add("Kỹ năng trùng: " + string.Join(", ", ms) + ".");

        return parts.Count == 0 ? "Phù hợp với nhu cầu tuyển thành viên của nhóm." : string.Join(" ", parts);
    }

    private static string BuildTopicFallbackReason(TopicSuggestionDto suggestion, IReadOnlyList<string> matches)
    {
        var parts = new List<string>();
        var ms = matches.Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList();
        if (ms.Count > 0)
            parts.Add("Kỹ năng phù hợp: " + string.Join(", ", ms) + ".");
        if (suggestion.CanTakeMore)
            parts.Add("Mentor vẫn còn có thể nhận thêm nhóm.");
        return parts.Count == 0 ? "Topic phù hợp với nhu cầu và kỹ năng của nhóm." : string.Join(" ", parts);
    }

    private static IReadOnlyDictionary<string, string>? BuildLlmContext(params (string Key, string? Value)[] entries)
    {
        var buffer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rawValue) in entries)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(rawValue))
                continue;
            buffer[key] = rawValue;
        }

        return buffer.Count == 0 ? null : buffer;
    }

    private static IReadOnlyDictionary<string, string>? BuildMetadata(params (string Key, string? Value)[] entries)
    {
        var buffer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rawValue) in entries)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(rawValue))
                continue;
            buffer[key] = rawValue;
        }

        return buffer.Count == 0 ? null : buffer;
    }

    private static int NormalizeLlmScore(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;
        var clamped = Math.Clamp(value, 0, 100);
        return (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
    }

    private sealed record LlmRankResult<T>(T Item, double Score);

    private async Task<List<T>> ApplySemanticShortlistAsync<T>(
        string type,
        string queryText,
        Guid semesterId,
        Guid? majorId,
        IReadOnlyList<T> source,
        Func<T, Guid> idSelector,
        CancellationToken ct)
    {
        if (source.Count == 0 || string.IsNullOrWhiteSpace(queryText))
            return source.ToList();

        IReadOnlyList<Guid> shortlist = Array.Empty<Guid>();
        try
        {
            shortlist = await _semanticSearch.SearchIdsAsync(queryText, type, semesterId, majorId, SemanticShortlistLimit, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search failed for {Type}. Falling back to full set ({Count}).", type, source.Count);
            return source.ToList();
        }

        if (shortlist.Count == 0)
            return source.ToList();

        var shortlistSet = new HashSet<Guid>(shortlist);
        var filtered = source.Where(item => shortlistSet.Contains(idSelector(item))).ToList();

        if (filtered.Count == 0)
        {
            _logger.LogInformation("Semantic shortlist for {Type} returned {Shortlist} ids with no overlaps. Using original {Total} items.", type, shortlist.Count, source.Count);
            return source.ToList();
        }

        _logger.LogInformation(
            "Semantic shortlist for {Type}: {Shortlist} ids, reduced candidates to {Filtered}/{Total}.",
            type,
            shortlist.Count,
            filtered.Count,
            source.Count);

        return filtered;
    }

    private static IReadOnlyList<string> RemapMatchingSkills(IReadOnlyList<string> matches, IReadOnlyList<string> topicSkills)
    {
        if (matches.Count == 0 || topicSkills.Count == 0)
            return matches;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in topicSkills)
        {
            var key = NormalizeSkillKey(skill);
            if (key is not null && !map.ContainsKey(key))
                map[key] = skill;
        }

        if (map.Count == 0)
            return matches;

        var remapped = matches
            .Select(match => map.TryGetValue(NormalizeSkillKey(match) ?? string.Empty, out var display)
                ? display
                : match)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return remapped.Count == 0 ? matches : remapped;
    }

    private static (double Coverage, List<string> Matches) CalculateTopicSkillCoverage(
        IReadOnlyList<string> topicSkills,
        AiSkillProfile groupProfile)
    {
        var matches = new List<string>();
        if (topicSkills.Count == 0 || !groupProfile.HasTags)
            return (0, matches);

        var groupSkillKeys = BuildSkillKeySet(groupProfile.Tags);
        if (groupSkillKeys.Count == 0)
            return (0, matches);

        var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in topicSkills)
        {
            var key = NormalizeSkillKey(skill);
            if (key is null || !groupSkillKeys.Contains(key) || !matchedKeys.Add(key))
                continue;

            matches.Add(skill);
        }

        var coverage = topicSkills.Count == 0 ? 0 : (double)matches.Count / topicSkills.Count;
        return (coverage, matches);
    }

    private static HashSet<string> BuildSkillKeySet(IEnumerable<string> tags)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var key = NormalizeSkillKey(tag);
            if (key is not null)
                keys.Add(key);
        }

        return keys;
    }

    private static string? NormalizeSkillKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().ToLowerInvariant();
    }

    private Task RefreshAssignmentCachesAsync(CancellationToken ct)
        => aiQueries.RefreshStudentsPoolAsync(ct);

    private static AiSkillProfile BuildSkillProfile(StudentProfileSnapshot student)
    {
        var parsed = AiSkillProfile.FromJson(student.SkillsJson);
        var fallbackRole = AiRoleHelper.Parse(student.PrimaryRole);
        if (parsed.PrimaryRole == AiPrimaryRole.Unknown && fallbackRole != AiPrimaryRole.Unknown)
            parsed = parsed with { PrimaryRole = fallbackRole };
        return parsed;
    }

    private static int NormalizeScoreToPercent(int score, int threshold, int maxScore)
    {
        if (maxScore <= threshold)
            return 100;

        var clamped = Math.Clamp(score, threshold, maxScore);
        var normalized = (double)(clamped - threshold) / (maxScore - threshold);
        return (int)Math.Round(normalized * 100, MidpointRounding.AwayFromZero);
    }

    private static RecruitmentPostSuggestionDto? BuildPostSuggestion(
        AiSkillProfile studentProfile,
        Guid studentMajorId,
        RecruitmentPostSnapshot post)
    {
        var requiredProfile = AiSkillProfile.FromJson(post.RequiredSkills);
        var requiredSkillTags = requiredProfile.HasTags
            ? requiredProfile.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : null;
        if (!requiredProfile.HasTags && !string.IsNullOrWhiteSpace(post.PositionNeeded))
            requiredProfile = AiSkillProfile.FromText(post.PositionNeeded);

        var matchingSkills = studentProfile.FindMatches(requiredProfile).ToList();
        if (matchingSkills.Count == 0 && !string.IsNullOrWhiteSpace(post.PositionNeeded))
        {
            var normalized = post.PositionNeeded!.ToLowerInvariant();
            matchingSkills = studentProfile.Tags
                .Where(tag => normalized.Contains(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        var overlapScore = Math.Min(matchingSkills.Count, 5) * 18;
        var roleScore = ScoreRoleMatch(studentProfile.PrimaryRole, requiredProfile.PrimaryRole, post.PositionNeeded);
        var majorBoost = post.MajorId.HasValue && post.MajorId.Value == studentMajorId ? 15 : 5;
        var recencyDays = Math.Clamp((int)(DateTime.UtcNow - post.CreatedAt).TotalDays, 0, 30);
        var recencyBoost = Math.Max(5, 30 - recencyDays);
        var totalScore = overlapScore + roleScore + majorBoost + recencyBoost;

        if (totalScore <= RecruitmentScoreThreshold)
            return null;

        var normalizedScore = NormalizeScoreToPercent(totalScore, RecruitmentScoreThreshold, RecruitmentScoreMax);
        return new RecruitmentPostSuggestionDto(
            post.PostId,
            post.Title,
            post.Description,
            post.GroupId,
            post.GroupName,
            post.MajorId,
            post.MajorName,
            post.CreatedAt,
            post.ApplicationDeadline,
            normalizedScore,
            post.PositionNeeded,
            requiredSkillTags,
            matchingSkills
        );
    }

    private static int CalculateRoleNeedAdjustment(AiPrimaryRole candidateRole, GroupRoleMixSnapshot? mix)
    {
        if (mix is null || candidateRole == AiPrimaryRole.Unknown)
            return 0;

        var frontendHeavy = mix.FrontendCount >= 2
                            && mix.FrontendCount >= mix.BackendCount + 1
                            && mix.FrontendCount >= mix.OtherCount + 1;

        var backendNeeded = mix.BackendCount == 0
                            || (mix.FrontendCount >= 2 && mix.BackendCount + 1 <= mix.FrontendCount - 1);
        var mobileNeeded = mix.OtherCount == 0 && mix.FrontendCount >= 2;

        var adjustment = 0;

        if (backendNeeded && candidateRole == AiPrimaryRole.Backend)
            adjustment += mix.BackendCount == 0 ? 28 : 20;

        if (mobileNeeded && candidateRole == AiPrimaryRole.Other)
            adjustment += 18;

        if (frontendHeavy && candidateRole == AiPrimaryRole.Frontend)
            adjustment -= mix.FrontendCount >= 3 ? 25 : 15;

        return adjustment;
    }

    private static TopicSuggestionDto? BuildTopicSuggestion(TopicAvailabilitySnapshot topic, AiSkillProfile groupProfile)
    {
        var topicProfile = AiSkillProfile.FromJson(topic.SkillsJson);
        var searchable = $"{topic.Title} {topic.Description}".ToLowerInvariant();
        var inferredRole = InferRoleFromText(searchable);

        List<string> matchingSkills;
        int matchScore;

        if (topic.SkillNames.Count > 0 && groupProfile.HasTags)
        {
            var (coverage, canonicalMatches) = CalculateTopicSkillCoverage(topic.SkillNames, groupProfile);
            matchingSkills = canonicalMatches;
            matchScore = (int)Math.Round(coverage * TopicSkillCoverageWeight, MidpointRounding.AwayFromZero);
        }
        else if (topicProfile.HasTags)
        {
            if (groupProfile.HasTags)
            {
                matchingSkills = groupProfile.FindMatches(topicProfile)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                matchScore = Math.Min(matchingSkills.Count, 6) * 18;
            }
            else
            {
                matchingSkills = topicProfile.Tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                matchScore = Math.Min(matchingSkills.Count, 6) * 10;
            }
        }
        else
        {
            matchingSkills = groupProfile.HasTags
                ? groupProfile.Tags
                    .Where(tag => searchable.Contains(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            matchScore = Math.Min(matchingSkills.Count, 6) * 12;
            if (matchingSkills.Count == 0 && !groupProfile.HasTags)
                matchScore = 8;
        }

        var roleScore = ScoreRoleMatch(groupProfile.PrimaryRole, topicProfile.PrimaryRole == AiPrimaryRole.Unknown ? inferredRole : topicProfile.PrimaryRole, searchable);
        var capacityBoost = topic.CanTakeMore ? 10 : 0;
        var totalScore = matchScore + roleScore + capacityBoost;

        if (totalScore <= TopicScoreThreshold)
            return null;

        var normalizedScore = NormalizeScoreToPercent(totalScore, TopicScoreThreshold, TopicScoreMax);
        var displayMatches = RemapMatchingSkills(matchingSkills, topic.SkillNames);
        return new TopicSuggestionDto(topic.TopicId, topic.Title, topic.Description, normalizedScore, topic.CanTakeMore, displayMatches, topic.SkillNames);
    }

    private static ProfilePostSuggestionDto? BuildProfilePostSuggestion(
        ProfilePostSnapshot post,
        AiSkillProfile needsProfile,
        AiSkillProfile groupProfile,
        GroupRoleMixSnapshot mix)
    {
        var candidateProfile = AiSkillProfile.FromJson(post.SkillsJson);
        if (!candidateProfile.HasTags && !string.IsNullOrWhiteSpace(post.SkillsText))
            candidateProfile = AiSkillProfile.FromText(post.SkillsText);

        var primaryProfile = needsProfile.HasTags || needsProfile.PrimaryRole != AiPrimaryRole.Unknown
            ? needsProfile
            : groupProfile;

        var matchingSkills = candidateProfile.FindMatches(primaryProfile).ToList();
        if (matchingSkills.Count == 0 && primaryProfile != groupProfile)
            matchingSkills = candidateProfile.FindMatches(groupProfile).ToList();

        if (matchingSkills.Count == 0 && candidateProfile.HasTags)
        {
            matchingSkills = candidateProfile.Tags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        var needsFrontend = mix.FrontendCount == 0;
        var needsBackend = mix.BackendCount == 0;
        var role = candidateProfile.PrimaryRole;

        var searchable = string.Join(" ", new[]
        {
            post.Title,
            post.Description,
            post.SkillsText
        }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
        var isFullStack = searchable.Contains("fullstack")
                          || searchable.Contains("full stack")
                          || searchable.Contains("full-stack");

        var roleScore = 20;
        if (needsFrontend || needsBackend)
        {
            if (isFullStack && (needsFrontend || needsBackend))
                roleScore = 40;
            else if (role == AiPrimaryRole.Frontend && needsFrontend)
                roleScore = 40;
            else if (role == AiPrimaryRole.Backend && needsBackend)
                roleScore = 40;
            else if (role == AiPrimaryRole.Unknown)
                roleScore = 15;
            else
                roleScore = 10;
        }
        else if (role == AiPrimaryRole.Unknown)
        {
            roleScore = 12;
        }

        var overlapScore = Math.Min(matchingSkills.Count, 5) * 12;
        var recencyDays = Math.Clamp((int)(DateTime.UtcNow - post.CreatedAt).TotalDays, 0, 30);
        var recencyBoost = Math.Max(5, 30 - recencyDays);
        var totalScore = roleScore + overlapScore + recencyBoost;

        if (totalScore <= ProfileScoreThreshold)
            return null;

        var normalizedScore = NormalizeScoreToPercent(totalScore, ProfileScoreThreshold, ProfileScoreMax);
        var displayRole = isFullStack
            ? "fullstack"
            : AiRoleHelper.ToDisplayString(role);

        var inferredNeeds = new List<string>(2);
        if (needsFrontend)
            inferredNeeds.Add("frontend");
        if (needsBackend)
            inferredNeeds.Add("backend");

        var reasonParts = new List<string>(3);
        if (inferredNeeds.Count > 0)
        {
            // If group is missing FE/BE, explicitly call out that match.
            if (isFullStack || (needsFrontend && role == AiPrimaryRole.Frontend) || (needsBackend && role == AiPrimaryRole.Backend))
                reasonParts.Add($"Nhóm đang thiếu {string.Join("/", inferredNeeds)}; vai trò {displayRole} phù hợp.");
            else
                reasonParts.Add($"Nhóm đang thiếu {string.Join("/", inferredNeeds)}; ưu tiên các hồ sơ đúng vai trò hoặc fullstack.");
        }
        else if (!string.IsNullOrWhiteSpace(displayRole))
        {
            reasonParts.Add($"Vai trò: {displayRole}.");
        }

        if (matchingSkills.Count > 0)
            reasonParts.Add($"Kỹ năng trùng: {string.Join(", ", matchingSkills.Take(6))}.");

        var heuristicReason = reasonParts.Count == 0 ? null : string.Join(" ", reasonParts);

        return new ProfilePostSuggestionDto(
            post.PostId,
            post.OwnerUserId,
            post.OwnerDisplayName,
            post.Title,
            post.Description,
            post.MajorId,
            post.CreatedAt,
            normalizedScore,
            post.SkillsText,
            displayRole,
            matchingSkills)
        {
            AiReason = heuristicReason
        };
    }

    private static int ScoreRoleMatch(AiPrimaryRole studentRole, AiPrimaryRole requiredRole, string? textHint)
    {
        if (studentRole == AiPrimaryRole.Unknown)
            return 5;

        if (requiredRole != AiPrimaryRole.Unknown)
            return studentRole == requiredRole ? 35 : 5;

        var inferred = InferRoleFromText(textHint);
        return inferred != AiPrimaryRole.Unknown && inferred == studentRole ? 25 : 5;
    }

    private static AiPrimaryRole InferRoleFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return AiPrimaryRole.Unknown;

        var normalized = text.ToLowerInvariant();
        if (normalized.Contains("frontend") || normalized.Contains("ui") || normalized.Contains("react") || normalized.Contains("figma") || normalized.Contains("flutter"))
            return AiPrimaryRole.Frontend;
        if (normalized.Contains("backend") || normalized.Contains("api") || normalized.Contains("server") || normalized.Contains("database") || normalized.Contains("microservice"))
            return AiPrimaryRole.Backend;
        return AiPrimaryRole.Other;
    }

    private async Task<SemesterContext> ResolveSemesterAsync(Guid? semesterId, CancellationToken ct)
    {
        SemesterDetailDto? detail = semesterId.HasValue
            ? await semesterQueries.GetByIdAsync(semesterId.Value, ct)
            : await semesterQueries.GetActiveAsync(ct);

        if (detail is null)
            throw new InvalidOperationException("Không tìm thấy học kỳ.");
        if (detail.Policy is null)
            throw new InvalidOperationException("Học kỳ chưa cấu hình policy.");

        var season = string.IsNullOrWhiteSpace(detail.Season) ? "Semester" : detail.Season;
        var label = detail.Year > 0 ? $"{season} {detail.Year}" : season;
        return new SemesterContext(detail.SemesterId, detail.Policy, label);
    }

    private sealed record SemesterContext(Guid SemesterId, SemesterPolicyDto Policy, string Name);

    private static void EnsureWindow(DateOnly today, DateOnly start, DateOnly end, string errorMessage)
    {
        if (today < start || today > end)
            throw new InvalidOperationException(errorMessage);
    }

    private static int NormalizeLimit(int? limit)
    {
        if (!limit.HasValue || limit.Value <= 0)
            return DefaultSuggestionLimit;
        return Math.Min(limit.Value, MaxSuggestionLimit);
    }

    private static int NormalizeTopicSuggestionLimit(int? limit)
    {
        // Theo yêu cầu: đề xuất topic nên ổn định 4-6 (default 5)
        if (!limit.HasValue || limit.Value <= 0)
            return 5;
        return Math.Clamp(limit.Value, 4, 6);
    }

    private sealed record CandidateSelection(StudentProfileSnapshot Profile, AiPrimaryRole Role)
    {
        public Guid UserId => Profile.UserId;
    }

    private sealed class RolePools
    {
        // GPA balancing goal: spread high-GPA students across groups
        // so a single group doesn't receive all high-GPA members.
        private readonly double? _highGpaThreshold;

        private readonly Queue<StudentProfileSnapshot> _frontendHigh;
        private readonly Queue<StudentProfileSnapshot> _frontendMid;
        private readonly Queue<StudentProfileSnapshot> _frontendLow;

        private readonly Queue<StudentProfileSnapshot> _backendHigh;
        private readonly Queue<StudentProfileSnapshot> _backendMid;
        private readonly Queue<StudentProfileSnapshot> _backendLow;

        private readonly Queue<StudentProfileSnapshot> _othersHigh;
        private readonly Queue<StudentProfileSnapshot> _othersMid;
        private readonly Queue<StudentProfileSnapshot> _othersLow;

        public RolePools(IEnumerable<StudentProfileSnapshot> students)
        {
            var list = students.ToList();
            _highGpaThreshold = ComputeHighGpaThreshold(list);

            _frontendHigh = new Queue<StudentProfileSnapshot>();
            _frontendMid = new Queue<StudentProfileSnapshot>();
            _frontendLow = new Queue<StudentProfileSnapshot>();

            _backendHigh = new Queue<StudentProfileSnapshot>();
            _backendMid = new Queue<StudentProfileSnapshot>();
            _backendLow = new Queue<StudentProfileSnapshot>();

            _othersHigh = new Queue<StudentProfileSnapshot>();
            _othersMid = new Queue<StudentProfileSnapshot>();
            _othersLow = new Queue<StudentProfileSnapshot>();

            foreach (var student in list)
            {
                var tier = ClassifyGpa(student.Gpa, _highGpaThreshold);
                switch (AiRoleHelper.Parse(student.PrimaryRole))
                {
                    case AiPrimaryRole.Frontend:
                        EnqueueTiered(_frontendHigh, _frontendMid, _frontendLow, student, tier);
                        break;
                    case AiPrimaryRole.Backend:
                        EnqueueTiered(_backendHigh, _backendMid, _backendLow, student, tier);
                        break;
                    default:
                        EnqueueTiered(_othersHigh, _othersMid, _othersLow, student, tier);
                        break;
                }
            }
        }

        public int RemainingCount =>
            _frontendHigh.Count + _frontendMid.Count + _frontendLow.Count
            + _backendHigh.Count + _backendMid.Count + _backendLow.Count
            + _othersHigh.Count + _othersMid.Count + _othersLow.Count;

        public double? HighGpaThreshold => _highGpaThreshold;

        public bool IsHighGpa(StudentProfileSnapshot profile)
            => _highGpaThreshold.HasValue && profile.Gpa.HasValue && profile.Gpa.Value >= _highGpaThreshold.Value;

        public bool HasFrontendCandidates => _frontendHigh.Count + _frontendMid.Count + _frontendLow.Count > 0;
        public bool HasBackendCandidates => _backendHigh.Count + _backendMid.Count + _backendLow.Count > 0;
        public bool HasOtherCandidates => _othersHigh.Count + _othersMid.Count + _othersLow.Count > 0;

        public CandidateSelection? DequeueForGroup(GroupAssignmentState group)
        {
            AiPrimaryRole? priorityRole = null;

            if (!group.HasFrontend)
                priorityRole = AiPrimaryRole.Frontend;
            else if (!group.HasBackend)
                priorityRole = AiPrimaryRole.Backend;

            if (priorityRole.HasValue)
            {
                var pick = Dequeue(priorityRole.Value, group);
                if (pick is not null)
                    return pick;
            }

            return DequeueLargest();
        }

        private CandidateSelection? Dequeue(AiPrimaryRole desired, GroupAssignmentState group)
        {
            var avoidHigh = group.HighGpaAssignedCount > 0;

            return desired switch
            {
                AiPrimaryRole.Frontend => DequeueTiered(_frontendHigh, _frontendMid, _frontendLow, AiPrimaryRole.Frontend, avoidHigh),
                AiPrimaryRole.Backend => DequeueTiered(_backendHigh, _backendMid, _backendLow, AiPrimaryRole.Backend, avoidHigh),
                AiPrimaryRole.Other => DequeueTiered(_othersHigh, _othersMid, _othersLow, AiPrimaryRole.Other, avoidHigh),
                _ => null
            };
        }

        private CandidateSelection? DequeueLargest()
        {
            var feCount = _frontendHigh.Count + _frontendMid.Count + _frontendLow.Count;
            var beCount = _backendHigh.Count + _backendMid.Count + _backendLow.Count;
            var otherCount = _othersHigh.Count + _othersMid.Count + _othersLow.Count;

            if (feCount >= beCount && feCount >= otherCount && feCount > 0)
                return DequeueTiered(_frontendHigh, _frontendMid, _frontendLow, AiPrimaryRole.Frontend, avoidHigh: false);

            if (beCount >= otherCount && beCount > 0)
                return DequeueTiered(_backendHigh, _backendMid, _backendLow, AiPrimaryRole.Backend, avoidHigh: false);

            return otherCount > 0
                ? DequeueTiered(_othersHigh, _othersMid, _othersLow, AiPrimaryRole.Other, avoidHigh: false)
                : null;
        }

        public IEnumerable<Guid> RemainingStudentIds
            => _frontendHigh.Select(s => s.UserId)
                .Concat(_frontendMid.Select(s => s.UserId))
                .Concat(_frontendLow.Select(s => s.UserId))
                .Concat(_backendHigh.Select(s => s.UserId))
                .Concat(_backendMid.Select(s => s.UserId))
                .Concat(_backendLow.Select(s => s.UserId))
                .Concat(_othersHigh.Select(s => s.UserId))
                .Concat(_othersMid.Select(s => s.UserId))
                .Concat(_othersLow.Select(s => s.UserId));

        public IEnumerable<StudentProfileSnapshot> PeekFrontend(int max)
            => _frontendHigh.Concat(_frontendMid).Concat(_frontendLow).Take(Math.Max(0, max));

        public IEnumerable<StudentProfileSnapshot> PeekBackend(int max)
            => _backendHigh.Concat(_backendMid).Concat(_backendLow).Take(Math.Max(0, max));

        public IEnumerable<StudentProfileSnapshot> PeekOther(int max)
            => _othersHigh.Concat(_othersMid).Concat(_othersLow).Take(Math.Max(0, max));

        public CandidateSelection? DequeueSpecific(Guid userId)
        {
            if (userId == Guid.Empty)
                return null;

            CandidateSelection? TakeFromQueue(Queue<StudentProfileSnapshot> q, AiPrimaryRole role)
            {
                if (q.Count == 0)
                    return null;

                var found = false;
                var buffer = new Queue<StudentProfileSnapshot>(q.Count);
                CandidateSelection? result = null;

                while (q.Count > 0)
                {
                    var item = q.Dequeue();
                    if (!found && item.UserId == userId)
                    {
                        found = true;
                        result = new CandidateSelection(item, role);
                        continue;
                    }
                    buffer.Enqueue(item);
                }

                while (buffer.Count > 0)
                    q.Enqueue(buffer.Dequeue());

                return result;
            }

            return TakeFromQueue(_frontendHigh, AiPrimaryRole.Frontend)
                   ?? TakeFromQueue(_frontendMid, AiPrimaryRole.Frontend)
                   ?? TakeFromQueue(_frontendLow, AiPrimaryRole.Frontend)
                   ?? TakeFromQueue(_backendHigh, AiPrimaryRole.Backend)
                   ?? TakeFromQueue(_backendMid, AiPrimaryRole.Backend)
                   ?? TakeFromQueue(_backendLow, AiPrimaryRole.Backend)
                   ?? TakeFromQueue(_othersHigh, AiPrimaryRole.Other)
                   ?? TakeFromQueue(_othersMid, AiPrimaryRole.Other)
                   ?? TakeFromQueue(_othersLow, AiPrimaryRole.Other);
        }

        private enum GpaTier
        {
            Low,
            Mid,
            High
        }

        private static void EnqueueTiered(
            Queue<StudentProfileSnapshot> high,
            Queue<StudentProfileSnapshot> mid,
            Queue<StudentProfileSnapshot> low,
            StudentProfileSnapshot student,
            GpaTier tier)
        {
            switch (tier)
            {
                case GpaTier.High:
                    high.Enqueue(student);
                    break;
                case GpaTier.Low:
                    low.Enqueue(student);
                    break;
                default:
                    mid.Enqueue(student);
                    break;
            }
        }

        private static CandidateSelection? DequeueTiered(
            Queue<StudentProfileSnapshot> high,
            Queue<StudentProfileSnapshot> mid,
            Queue<StudentProfileSnapshot> low,
            AiPrimaryRole role,
            bool avoidHigh)
        {
            if (!avoidHigh && high.Count > 0)
                return new CandidateSelection(high.Dequeue(), role);
            if (mid.Count > 0)
                return new CandidateSelection(mid.Dequeue(), role);
            if (low.Count > 0)
                return new CandidateSelection(low.Dequeue(), role);
            return high.Count > 0 ? new CandidateSelection(high.Dequeue(), role) : null;
        }

        private static double? ComputeHighGpaThreshold(IReadOnlyList<StudentProfileSnapshot> students)
        {
            var gpas = students
                .Select(s => s.Gpa)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .OrderBy(x => x)
                .ToList();

            if (gpas.Count < 6)
                return null;

            var idx = (int)Math.Floor((gpas.Count - 1) * 0.75);
            idx = Math.Clamp(idx, 0, gpas.Count - 1);
            return gpas[idx];
        }

        private static GpaTier ClassifyGpa(double? gpa, double? highThreshold)
        {
            if (!gpa.HasValue || !highThreshold.HasValue)
                return GpaTier.Mid;

            if (gpa.Value >= highThreshold.Value)
                return GpaTier.High;

            return gpa.Value <= Math.Max(0, highThreshold.Value - 1.0) ? GpaTier.Low : GpaTier.Mid;
        }
    }

    private sealed class GroupAssignmentState
    {
        private GroupRoleMixSnapshot _mix;

        public GroupAssignmentState(GroupCapacitySnapshot source, GroupRoleMixSnapshot mix)
        {
            GroupId = source.GroupId;
            SemesterId = source.SemesterId;
            MajorId = source.MajorId;
            Name = source.Name;
            RemainingSlots = source.RemainingSlots;
            MaxMembers = source.MaxMembers;
            CurrentMembers = source.CurrentMembers;
            _mix = mix;
        }

        public Guid GroupId { get; }
        public Guid SemesterId { get; }
        public Guid? MajorId { get; }
        public string Name { get; }
        public int RemainingSlots { get; private set; }
        public int MaxMembers { get; private set; }
        public int CurrentMembers { get; private set; }
        public int AddedCapacity { get; private set; }
        public bool CapacityDirty { get; private set; }
        public bool HasFrontend => _mix.FrontendCount > 0;
        public bool HasBackend => _mix.BackendCount > 0;
        public int FrontendCount => _mix.FrontendCount;
        public int BackendCount => _mix.BackendCount;
        public int OtherCount => _mix.OtherCount;

        public int HighGpaAssignedCount { get; private set; }

        public bool TryExpand(int policyMax, int desiredSlots)
        {
            if (desiredSlots <= 0)
                return false;

            if (policyMax <= MaxMembers)
                return false;

            var slots = Math.Min(policyMax - MaxMembers, desiredSlots);
            if (slots <= 0)
                return false;

            MaxMembers += slots;
            RemainingSlots += slots;
            AddedCapacity += slots;
            CapacityDirty = true;
            return true;
        }

        public void EnsurePolicyRange(int policyMin, int policyMax)
        {
            var minBound = Math.Max(1, policyMin);
            var maxBound = Math.Max(minBound, policyMax);
            var clamped = MaxMembers;

            if (clamped < minBound)
                clamped = minBound;

            if (clamped > maxBound && maxBound >= CurrentMembers)
                clamped = maxBound;

            if (clamped == MaxMembers)
                return;

            MaxMembers = clamped;
            RemainingSlots = Math.Max(0, MaxMembers - CurrentMembers);
            CapacityDirty = true;
        }

        public void Apply(AiPrimaryRole role, bool isHighGpa)
        {
            if (RemainingSlots > 0)
                RemainingSlots--;
            CurrentMembers++;

            if (isHighGpa)
                HighGpaAssignedCount++;

            _mix = role switch
            {
                AiPrimaryRole.Frontend => _mix with { FrontendCount = _mix.FrontendCount + 1 },
                AiPrimaryRole.Backend => _mix with { BackendCount = _mix.BackendCount + 1 },
                AiPrimaryRole.Other => _mix with { OtherCount = _mix.OtherCount + 1 },
                _ => _mix
            };
        }

        public void ShrinkToRange(int policyMin, int policyMax)
        {
            var minBound = Math.Max(1, policyMin);
            var maxBound = Math.Max(minBound, policyMax);
            var targetMax = CurrentMembers < minBound ? minBound : CurrentMembers;
            if (targetMax > maxBound)
                targetMax = CurrentMembers;

            if (targetMax >= MaxMembers)
                return;

            MaxMembers = targetMax;
            RemainingSlots = Math.Max(0, MaxMembers - CurrentMembers);
            CapacityDirty = true;
        }
    }

}
