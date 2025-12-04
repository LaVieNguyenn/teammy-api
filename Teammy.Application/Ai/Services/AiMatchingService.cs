using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    IMajorReadOnlyQueries majorQueries)
{
    private const int DefaultSuggestionLimit = 5;
    private const int MaxSuggestionLimit = 20;
    private const int OptionSuggestionLimit = 3;
    private const int RecruitmentScoreThreshold = 20;
    private const int RecruitmentScoreMax = 220;
    private const int TopicScoreThreshold = 10;
    private const int TopicScoreMax = 110;
    private const int ProfileScoreThreshold = 20;
    private const int ProfileScoreMax = 140;

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

            var (groupOptions, studentOptions) = BuildStaffingOptions(groups, students, mixSnapshots, majorLookup);

            if (section is AiOptionSection.All or AiOptionSection.GroupsNeedingMembers)
                staffingPage = Paginate(groupOptions, page, pageSize);

            if (section is AiOptionSection.All or AiOptionSection.StudentsWithoutGroup)
                studentPage = Paginate(studentOptions, page, pageSize);
        }

        return new AiOptionListDto(
            semesterCtx.SemesterId,
            semesterCtx.Name,
            section,
            topicPage,
            staffingPage,
            studentPage);
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
        var targetMajorId = request.MajorId ?? profile.MajorId;
        var posts = await aiQueries.ListOpenRecruitmentPostsAsync(semesterCtx.SemesterId, targetMajorId, ct);
        if (posts.Count == 0)
            return Array.Empty<RecruitmentPostSuggestionDto>();

        var groupIds = posts
            .Where(p => p.GroupId.HasValue)
            .Select(p => p.GroupId!.Value)
            .Distinct()
            .ToArray();

        var mixes = groupIds.Length == 0
            ? new Dictionary<Guid, GroupRoleMixSnapshot>()
            : await aiQueries.GetGroupRoleMixAsync(groupIds, ct);

        var limit = NormalizeLimit(request.Limit);
        var suggestions = posts
            .Select(post =>
            {
                GroupRoleMixSnapshot? mix = null;
                if (post.GroupId is Guid gid && mixes.TryGetValue(gid, out var snapshot))
                    mix = snapshot;
                return BuildPostSuggestion(studentSkills, profile.MajorId, post, mix);
            })
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .OrderByDescending(dto => dto.Score)
            .Take(limit)
            .ToList();

        if (suggestions.Count == 0)
            return suggestions;

        return await HydrateRecruitmentPostSuggestionsAsync(suggestions, studentId, ct);
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

        var aggregatedProfile = members
            .Select(m => AiSkillProfile.FromJson(m.SkillsJson))
            .Where(p => p.HasTags || p.PrimaryRole != AiPrimaryRole.Unknown)
            .ToList();

        var groupSkillProfile = aggregatedProfile.Count == 0
            ? AiSkillProfile.Empty
            : AiSkillProfile.Combine(aggregatedProfile);

        var available = await aiQueries.ListTopicAvailabilityAsync(detail.SemesterId, detail.MajorId, ct);
        if (available.Count == 0)
            return Array.Empty<TopicSuggestionDto>();

        var limitValue = NormalizeLimit(limit);
        var items = available
            .Select(topic => BuildTopicSuggestion(topic, groupSkillProfile))
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .OrderByDescending(dto => dto.Score)
            .Take(limitValue)
            .ToList();

        if (items.Count == 0)
            return items;

        return await HydrateTopicSuggestionsAsync(items, ct);
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
        var aggregatedProfile = members
            .Select(m => AiSkillProfile.FromJson(m.SkillsJson))
            .Where(p => p.HasTags || p.PrimaryRole != AiPrimaryRole.Unknown)
            .ToList();

        var groupSkillProfile = aggregatedProfile.Count == 0
            ? AiSkillProfile.Empty
            : AiSkillProfile.Combine(aggregatedProfile);

        var mixes = await aiQueries.GetGroupRoleMixAsync(new[] { request.GroupId }, ct);
        var mix = mixes.TryGetValue(request.GroupId, out var snapshot)
            ? snapshot
            : new GroupRoleMixSnapshot(request.GroupId, 0, 0, 0);

        var posts = await aiQueries.ListOpenProfilePostsAsync(detail.SemesterId, detail.MajorId, ct);
        if (posts.Count == 0)
            return Array.Empty<ProfilePostSuggestionDto>();

        var limit = NormalizeLimit(request.Limit);
        var suggestions = posts
            .Select(post => BuildProfilePostSuggestion(post, groupSkillProfile, mix))
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .OrderByDescending(dto => dto.Score)
            .Take(limit)
            .ToList();

        if (suggestions.Count == 0)
            return suggestions;

        return await HydrateProfilePostSuggestionsAsync(suggestions, currentUserId, ct);
    }

    public async Task<AutoAssignTeamsResultDto> AutoAssignTeamsAsync(Guid currentUserId, AutoAssignTeamsRequest? request, CancellationToken ct)
    {
        request ??= new AutoAssignTeamsRequest(null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(null, ct);
        var phase = await AssignStudentsToGroupsAsync(semesterCtx.SemesterId, request.MajorId, request.Limit, ct);
        var majorLookup = (await majorQueries.ListAsync(ct)).ToDictionary(x => x.MajorId, x => x.MajorName);
        var newGroups = await CreateNewGroupsForStudentsAsync(phase.RemainingStudents, semesterCtx, currentUserId, request.MajorId, majorLookup, ct);

        var combinedAssignments = phase.Assignments.Concat(newGroups.Assignments).ToList();

        if (combinedAssignments.Count > 0)
            await RefreshAssignmentCachesAsync(ct);

        return new AutoAssignTeamsResultDto(
            combinedAssignments.Count,
            combinedAssignments,
            newGroups.UnresolvedStudentIds,
            phase.OpenGroupIds,
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
            var assignment = await AssignTopicToGroupAsync(request.GroupId.Value, currentUserId, enforceMembership: true, request.LimitPerGroup, throwIfUnavailable: true, ct)
                ?? throw new InvalidOperationException("Không tìm thấy topic phù hợp.");
            return new AutoAssignTopicBatchResultDto(1, new[] { assignment }, Array.Empty<Guid>());
        }

        if (!canManageAllGroups)
            throw new UnauthorizedAccessException("Chỉ admin hoặc moderator mới được phép auto assign cho toàn bộ nhóm.");

        var semesterId = await groupQueries.GetActiveSemesterIdAsync(ct)
            ?? throw new InvalidOperationException("Không tìm thấy học kỳ.");

        var phase = await AssignTopicsForEligibleGroupsAsync(semesterId, request.MajorId, currentUserId, request.LimitPerGroup, ct);
        return new AutoAssignTopicBatchResultDto(phase.Assignments.Count, phase.Assignments, phase.SkippedGroupIds);
    }

    public async Task<AiAutoResolveResultDto> AutoResolveAsync(
        Guid currentUserId,
        AiAutoResolveRequest? request,
        CancellationToken ct)
    {
        request ??= new AiAutoResolveRequest(null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(request.SemesterId, ct);

        var studentPhase = await AssignStudentsToGroupsAsync(semesterCtx.SemesterId, request.MajorId, null, ct);
        var topicPhase = await AssignTopicsForEligibleGroupsAsync(semesterCtx.SemesterId, request.MajorId, currentUserId, null, ct);
        var majorLookup = (await majorQueries.ListAsync(ct)).ToDictionary(x => x.MajorId, x => x.MajorName);
        var newGroupsPhase = await CreateNewGroupsForStudentsAsync(studentPhase.RemainingStudents, semesterCtx, currentUserId, request.MajorId, majorLookup, ct);

        var totalAssignments = studentPhase.Assignments.Count + newGroupsPhase.Assignments.Count;
        var totalTopics = topicPhase.Assignments.Count + newGroupsPhase.TopicAssignments.Count;
        var combinedAssignments = studentPhase.Assignments.Concat(newGroupsPhase.Assignments).ToList();
        var combinedTopicAssignments = topicPhase.Assignments.Concat(newGroupsPhase.TopicAssignments).ToList();
        var skippedTopics = topicPhase.SkippedGroupIds.Concat(newGroupsPhase.TopicFailures).Distinct().ToList();

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
            newGroupsPhase.Groups,
            newGroupsPhase.UnresolvedStudentIds);
    }

    private async Task<StudentAssignmentPhaseResult> AssignStudentsToGroupsAsync(
        Guid semesterId,
        Guid? majorId,
        int? requestLimit,
        CancellationToken ct)
    {
        var students = await aiQueries.ListUnassignedStudentsAsync(semesterId, majorId, ct);
        if (students.Count == 0)
            return new StudentAssignmentPhaseResult(Array.Empty<AutoAssignmentRecordDto>(), Array.Empty<StudentProfileSnapshot>(), Array.Empty<Guid>());

        var groups = await aiQueries.ListGroupCapacitiesAsync(semesterId, majorId, ct);
        if (groups.Count == 0)
            return new StudentAssignmentPhaseResult(Array.Empty<AutoAssignmentRecordDto>(), students, Array.Empty<Guid>());

        var mixes = await aiQueries.GetGroupRoleMixAsync(groups.Select(g => g.GroupId), ct);
        var limit = requestLimit.HasValue && requestLimit.Value > 0 ? requestLimit.Value : int.MaxValue;

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

        foreach (var (majorKey, groupStates) in groupsByMajor)
        {
            if (!studentsByMajor.TryGetValue(majorKey, out var pools))
                continue;

            foreach (var groupState in groupStates)
            {
                while (groupState.RemainingSlots > 0 && limit > 0)
                {
                    var candidate = pools.DequeueForGroup(groupState);
                    if (candidate is null)
                        break;

                    await groupRepository.AddMembershipAsync(groupState.GroupId, candidate.UserId, groupState.SemesterId, "member", ct);
                    await postRepository.DeleteProfilePostsForUserAsync(candidate.UserId, groupState.SemesterId, ct);
                    assignments.Add(new AutoAssignmentRecordDto(
                        candidate.UserId,
                        groupState.GroupId,
                        groupState.Name,
                        AiRoleHelper.ToDisplayString(candidate.Role)));

                    groupState.Apply(candidate.Role);
                    limit--;
                }
            }
        }

        var remainingIds = studentsByMajor.Values
            .SelectMany(p => p.RemainingStudentIds)
            .Distinct()
            .ToList();
        var remainingSet = new HashSet<Guid>(remainingIds);
        var remainingSnapshots = students.Where(s => remainingSet.Contains(s.UserId)).ToList();

        var openGroups = groupsByMajor.Values
            .SelectMany(g => g)
            .Where(state => state.RemainingSlots > 0)
            .Select(state => state.GroupId)
            .Distinct()
            .ToList();

        return new StudentAssignmentPhaseResult(assignments, remainingSnapshots, openGroups);
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
                        && g.CurrentMembers >= g.MaxMembers)
            .Select(g => g.Id)
            .Distinct()
            .ToList();

        if (targets.Count == 0)
            return new TopicAssignmentPhaseResult(Array.Empty<AutoAssignTopicResultDto>(), Array.Empty<Guid>());

        var assignments = new List<AutoAssignTopicResultDto>();
        var skipped = new List<Guid>();

        foreach (var groupId in targets)
        {
            try
            {
                var result = await AssignTopicToGroupAsync(groupId, actorUserId, enforceMembership: false, limitPerGroup, throwIfUnavailable: false, ct);
                if (result is not null)
                    assignments.Add(result);
                else
                    skipped.Add(groupId);
            }
            catch
            {
                skipped.Add(groupId);
            }
        }

        return new TopicAssignmentPhaseResult(assignments, skipped);
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
            var relevantTopics = group.MajorId.HasValue
                ? topicList.Where(t => !t.MajorId.HasValue || t.MajorId.Value == group.MajorId.Value)
                : topicList;

            var suggestionItems = relevantTopics
                .Select(topic =>
                {
                    var suggestion = BuildTopicSuggestion(topic, groupProfile);
                    if (suggestion is null)
                        return null;

                    var reason = BuildTopicReason(topic, suggestion, group);
                    return new TopicSuggestionDetailDto(
                        topic.TopicId,
                        topic.Title,
                        topic.Description,
                        suggestion.Score,
                        suggestion.MatchingSkills,
                        reason);
                })
                .Where(dto => dto is not null)
                .Select(dto => dto!)
                .OrderByDescending(dto => dto.Score)
                .Take(OptionSuggestionLimit)
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
                suggestionItems));
        }

        return new PaginatedCollection<GroupTopicOptionDto>(total, page, pageSize, items);
    }

    private async Task<AiSkillProfile> GetGroupSkillProfileAsync(Guid groupId, CancellationToken ct)
    {
        var members = await aiQueries.ListGroupMemberSkillsAsync(groupId, ct);
        if (members.Count == 0)
            return AiSkillProfile.Empty;

        var aggregated = members
            .Select(m => AiSkillProfile.FromJson(m.SkillsJson))
            .Where(p => p.HasTags || p.PrimaryRole != AiPrimaryRole.Unknown)
            .ToList();

        return aggregated.Count == 0 ? AiSkillProfile.Empty : AiSkillProfile.Combine(aggregated);
    }

    private (IReadOnlyList<GroupStaffingOptionDto> Groups, IReadOnlyList<StudentPlacementOptionDto> Students) BuildStaffingOptions(
        IReadOnlyList<GroupOverviewSnapshot> groups,
        IReadOnlyList<StudentProfileSnapshot> students,
        IReadOnlyDictionary<Guid, GroupRoleMixSnapshot> mixes,
        IReadOnlyDictionary<Guid, string> majors)
    {
        if (groups.Count == 0 && students.Count == 0)
            return (Array.Empty<GroupStaffingOptionDto>(), Array.Empty<StudentPlacementOptionDto>());

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

            while (slots > 0 && availablePool.Count > 0)
            {
                var (index, score) = FindBestCandidate(availablePool, group, mutableMix);
                if (index < 0)
                    break;

                var candidate = availablePool[index];
                availablePool.RemoveAt(index);

                var reason = BuildCandidateReason(group, mutableMix, candidate.Profile, candidate.Snapshot, score);
                suggestions.Add(new GroupCandidateSuggestionDto(
                    candidate.Snapshot.UserId,
                    candidate.Snapshot.DisplayName,
                    candidate.Snapshot.MajorId,
                    candidate.MajorName,
                    AiRoleHelper.ToDisplayString(candidate.Profile.PrimaryRole),
                    candidate.Profile.Tags.Take(5).ToList(),
                    score,
                    reason));

                placements[candidate.Snapshot.UserId] = new GroupPlacementSuggestionDto(
                    group.GroupId,
                    group.Name,
                    group.MajorId,
                    group.MajorName,
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
                suggestions));
        }

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
                    AiRoleHelper.ToDisplayString(candidate.Profile.PrimaryRole),
                    tags,
                    placement,
                    placement is null);
            })
            .ToList();

        return (groupOptions, studentOptions);
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
        var unresolved = new List<Guid>();
        var counter = 1;

        var groupedByMajor = remainingStudents
            .GroupBy(s => (Guid?)s.MajorId)
            .OrderBy(g => g.Key.HasValue ? 0 : 1)
            .ThenBy(g => g.Key ?? Guid.Empty)
            .ToList();

        foreach (var majorGroup in groupedByMajor)
        {
            var ordered = majorGroup
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            while (ordered.Count > 0)
            {
                var take = Math.Min(maxSize, ordered.Count);
                var remainingAfterTake = ordered.Count - take;
                if (remainingAfterTake > 0 && remainingAfterTake < minSize)
                {
                    var transferable = Math.Min(take - minSize, minSize - remainingAfterTake);
                    if (transferable > 0)
                    {
                        take -= transferable;
                        remainingAfterTake += transferable;
                    }
                }

                var batch = ordered.Take(take).ToList();
                ordered.RemoveRange(0, take);

                var groupName = await GenerateUniqueAutoGroupNameAsync(semesterCtx.SemesterId, counter, ct);
                counter++;
                var majorId = majorGroup.Key ?? DetermineGroupMajor(batch, preferredMajorId);
                var description = $"Nhóm được tạo tự động vào {DateTime.UtcNow:dd/MM/yyyy}.";
                var skillsJson = BuildGroupSkillsJson(batch);
                var groupId = await groupRepository.CreateGroupAsync(
                    semesterCtx.SemesterId,
                    null,
                    majorId,
                    groupName,
                    description,
                    maxSize,
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

                AutoAssignTopicResultDto? topicResult = null;
                try
                {
                    topicResult = await AssignTopicToGroupAsync(groupId, actorUserId, enforceMembership: false, OptionSuggestionLimit, throwIfUnavailable: false, ct);
                }
                catch
                {
                    // swallow and mark as failure below
                }

                if (topicResult is not null)
                    topicAssignments.Add(topicResult);
                else
                    topicFailures.Add(groupId);

                groups.Add(new AutoResolveNewGroupDto(
                    groupId,
                    groupName,
                    majorId,
                    GetMajorName(majorId, majorLookup),
                    batch.Count,
                    topicResult?.TopicId,
                    topicResult?.TopicTitle,
                    batch.Select(s => s.UserId).ToList()));
            }
        }

        return new NewGroupCreationResult(groups, assignments, topicAssignments, topicFailures, unresolved);
    }

    private static (int Index, int Score) FindBestCandidate(
        List<StudentCandidate> pool,
        GroupOverviewSnapshot group,
        GroupRoleMixSnapshot mix)
    {
        var bestIndex = -1;
        var bestScore = int.MinValue;

        for (var i = 0; i < pool.Count; i++)
        {
            var candidate = pool[i];
            var score = ScoreCandidateForGroup(candidate.Profile, candidate.Snapshot.MajorId, group, mix);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return (bestIndex, bestScore);
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

        reasons.Add($"Điểm AI {score}");
        return string.Join(" | ", reasons);
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
        return string.Join(" | ", reasons);
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
        IReadOnlyList<Guid> OpenGroupIds);

    private sealed record TopicAssignmentPhaseResult(
        IReadOnlyList<AutoAssignTopicResultDto> Assignments,
        IReadOnlyList<Guid> SkippedGroupIds);

    private sealed record NewGroupCreationResult(
        IReadOnlyList<AutoResolveNewGroupDto> Groups,
        IReadOnlyList<AutoAssignmentRecordDto> Assignments,
        IReadOnlyList<AutoAssignTopicResultDto> TopicAssignments,
        IReadOnlyList<Guid> TopicFailures,
        IReadOnlyList<Guid> UnresolvedStudentIds)
    {
        public static NewGroupCreationResult Empty { get; } = new(
            Array.Empty<AutoResolveNewGroupDto>(),
            Array.Empty<AutoAssignmentRecordDto>(),
            Array.Empty<AutoAssignTopicResultDto>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>());
    }

    private async Task<AutoAssignTopicResultDto?> AssignTopicToGroupAsync(
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
        {
            if (throwIfUnavailable)
                throw new InvalidOperationException("Nhóm đã có topic.");
            return null;
        }

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount < maxMembers)
        {
            if (throwIfUnavailable)
                throw new InvalidOperationException("Nhóm chưa đủ thành viên để chọn topic.");
            return null;
        }

        var suggestions = await SuggestTopicsInternalAsync(groupId, actorUserId, enforceMembership, limit, ct);
        if (suggestions.Count == 0)
        {
            if (throwIfUnavailable)
                throw new InvalidOperationException("Không tìm thấy topic phù hợp.");
            return null;
        }

        var chosen = suggestions.First();
        var mentorId = await topicQueries.GetDefaultMentorIdAsync(chosen.TopicId, ct);
        if (!mentorId.HasValue)
        {
            var topicDetail = await topicQueries.GetByIdAsync(chosen.TopicId, ct);
            mentorId = topicDetail?.Mentors.FirstOrDefault()?.MentorId;
        }

        if (!mentorId.HasValue)
        {
            if (throwIfUnavailable)
                throw new InvalidOperationException("Topic chưa cấu hình mentor.");
            return null;
        }

        await groupRepository.UpdateGroupAsync(groupId, null, null, null, null, chosen.TopicId, mentorId, null, ct);
        await groupRepository.SetStatusAsync(groupId, "active", ct);
        await topicWriteRepository.SetStatusAsync(chosen.TopicId, "closed", ct);
        await postRepository.CloseAllOpenPostsForGroupAsync(groupId, ct);

        return new AutoAssignTopicResultDto(groupId, chosen.TopicId, chosen.Title, chosen.Score);
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

    private Task RefreshAssignmentCachesAsync(CancellationToken ct)
        => Task.WhenAll(
            aiQueries.RefreshStudentsPoolAsync(ct),
            aiQueries.RefreshGroupCapacityAsync(ct));

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
        RecruitmentPostSnapshot post,
        GroupRoleMixSnapshot? mix)
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
        var needAdjustment = CalculateRoleNeedAdjustment(studentProfile.PrimaryRole, mix);
        var totalScore = overlapScore + roleScore + majorBoost + recencyBoost + needAdjustment;

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
        var searchable = $"{topic.Title} {topic.Description}".ToLowerInvariant();
        var matchingSkills = groupProfile.HasTags
            ? groupProfile.Tags.Where(tag => searchable.Contains(tag)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList()
            : new List<string>();

        var matchScore = Math.Min(matchingSkills.Count, 5) * 12;
        if (matchingSkills.Count == 0 && !groupProfile.HasTags)
            matchScore = 8; // fallback so groups without profile still get suggestions

        var roleScore = ScoreRoleMatch(groupProfile.PrimaryRole, InferRoleFromText(searchable), searchable);
        var capacityBoost = topic.CanTakeMore ? 10 : 0;
        var totalScore = matchScore + roleScore + capacityBoost;

        if (totalScore <= TopicScoreThreshold)
            return null;

        var normalizedScore = NormalizeScoreToPercent(totalScore, TopicScoreThreshold, TopicScoreMax);
        return new TopicSuggestionDto(topic.TopicId, topic.Title, topic.Description, normalizedScore, topic.CanTakeMore, matchingSkills);
    }

    private static ProfilePostSuggestionDto? BuildProfilePostSuggestion(
        ProfilePostSnapshot post,
        AiSkillProfile groupProfile,
        GroupRoleMixSnapshot mix)
    {
        var candidateProfile = AiSkillProfile.FromJson(post.SkillsJson);
        if (!candidateProfile.HasTags && !string.IsNullOrWhiteSpace(post.SkillsText))
            candidateProfile = AiSkillProfile.FromText(post.SkillsText);

        var matchingSkills = candidateProfile.FindMatches(groupProfile).ToList();
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
        var roleScore = role switch
        {
            AiPrimaryRole.Frontend when needsFrontend => 40,
            AiPrimaryRole.Backend when needsBackend => 40,
            AiPrimaryRole.Unknown => 15,
            _ => 20
        };

        var overlapScore = Math.Min(matchingSkills.Count, 5) * 12;
        var recencyDays = Math.Clamp((int)(DateTime.UtcNow - post.CreatedAt).TotalDays, 0, 30);
        var recencyBoost = Math.Max(5, 30 - recencyDays);
        var totalScore = roleScore + overlapScore + recencyBoost;

        if (totalScore <= ProfileScoreThreshold)
            return null;

        var normalizedScore = NormalizeScoreToPercent(totalScore, ProfileScoreThreshold, ProfileScoreMax);
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
            AiRoleHelper.ToDisplayString(role),
            matchingSkills);
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

    private sealed record CandidateSelection(StudentProfileSnapshot Profile, AiPrimaryRole Role)
    {
        public Guid UserId => Profile.UserId;
    }

    private sealed class RolePools
    {
        private readonly Queue<StudentProfileSnapshot> _frontend;
        private readonly Queue<StudentProfileSnapshot> _backend;
        private readonly Queue<StudentProfileSnapshot> _others;

        public RolePools(IEnumerable<StudentProfileSnapshot> students)
        {
            _frontend = new Queue<StudentProfileSnapshot>();
            _backend = new Queue<StudentProfileSnapshot>();
            _others = new Queue<StudentProfileSnapshot>();

            foreach (var student in students)
            {
                switch (AiRoleHelper.Parse(student.PrimaryRole))
                {
                    case AiPrimaryRole.Frontend:
                        _frontend.Enqueue(student);
                        break;
                    case AiPrimaryRole.Backend:
                        _backend.Enqueue(student);
                        break;
                    default:
                        _others.Enqueue(student);
                        break;
                }
            }
        }

        public CandidateSelection? DequeueForGroup(GroupAssignmentState group)
        {
            if (!group.HasFrontend)
            {
                var pick = Dequeue(AiPrimaryRole.Frontend);
                return pick;
            }

            if (!group.HasBackend)
            {
                var pick = Dequeue(AiPrimaryRole.Backend);
                return pick;
            }

            return DequeueLargest();
        }

        private CandidateSelection? Dequeue(AiPrimaryRole desired)
        {
            return desired switch
            {
                AiPrimaryRole.Frontend when _frontend.Count > 0 => new CandidateSelection(_frontend.Dequeue(), AiPrimaryRole.Frontend),
                AiPrimaryRole.Backend when _backend.Count > 0 => new CandidateSelection(_backend.Dequeue(), AiPrimaryRole.Backend),
                AiPrimaryRole.Other when _others.Count > 0 => new CandidateSelection(_others.Dequeue(), AiPrimaryRole.Other),
                _ => null
            };
        }

        private CandidateSelection? DequeueLargest()
        {
            if (_frontend.Count >= _backend.Count && _frontend.Count > 0)
                return new CandidateSelection(_frontend.Dequeue(), AiPrimaryRole.Frontend);
            if (_backend.Count > 0)
                return new CandidateSelection(_backend.Dequeue(), AiPrimaryRole.Backend);
            return _others.Count > 0 ? new CandidateSelection(_others.Dequeue(), AiPrimaryRole.Other) : null;
        }

        public IEnumerable<Guid> RemainingStudentIds
            => _frontend.Select(s => s.UserId)
                .Concat(_backend.Select(s => s.UserId))
                .Concat(_others.Select(s => s.UserId));
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
            _mix = mix;
        }

        public Guid GroupId { get; }
        public Guid SemesterId { get; }
        public Guid? MajorId { get; }
        public string Name { get; }
        public int RemainingSlots { get; private set; }
        public bool HasFrontend => _mix.FrontendCount > 0;
        public bool HasBackend => _mix.BackendCount > 0;

        public void Apply(AiPrimaryRole role)
        {
            if (RemainingSlots > 0)
                RemainingSlots--;

            _mix = role switch
            {
                AiPrimaryRole.Frontend => _mix with { FrontendCount = _mix.FrontendCount + 1 },
                AiPrimaryRole.Backend => _mix with { BackendCount = _mix.BackendCount + 1 },
                AiPrimaryRole.Other => _mix with { OtherCount = _mix.OtherCount + 1 },
                _ => _mix
            };
        }
    }

}
