using System.Linq;
using Teammy.Application.Ai.Dtos;
using Teammy.Application.Ai.Models;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Semesters.Dtos;

namespace Teammy.Application.Ai.Services;

public sealed class AiMatchingService(
    IAiMatchingQueries aiQueries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepository,
    ISemesterReadOnlyQueries semesterQueries)
{
    private const int DefaultSuggestionLimit = 5;
    private const int MaxSuggestionLimit = 20;

    public async Task<IReadOnlyList<TeamSuggestionDto>> SuggestTeamsForStudentAsync(
        Guid studentId,
        TeamSuggestionRequest request,
        CancellationToken ct)
    {
        request ??= new TeamSuggestionRequest(null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(request.SemesterId, ct);
        EnsureWindow(DateOnly.FromDateTime(DateTime.UtcNow),
            semesterCtx.Policy.TeamSuggestStart,
            semesterCtx.Policy.TeamSelfSelectEnd,
            "Thời gian tự ghép nhóm đã đóng, không thể dùng gợi ý AI.");

        var hasGroup = await groupQueries.HasActiveGroupAsync(studentId, semesterCtx.SemesterId, ct);
        if (hasGroup)
            throw new InvalidOperationException("Bạn đã có nhóm trong học kỳ này.");

        var profile = await aiQueries.GetStudentProfileAsync(studentId, semesterCtx.SemesterId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy hồ sơ sinh viên trong danh sách ghép tự động.");

        var groups = await aiQueries.ListGroupCapacitiesAsync(semesterCtx.SemesterId, profile.MajorId, ct);
        if (groups.Count == 0)
            return Array.Empty<TeamSuggestionDto>();

        var mixes = await aiQueries.GetGroupRoleMixAsync(groups.Select(g => g.GroupId), ct);
        var limit = NormalizeLimit(request.Limit);

        var items = groups
            .Select(g => BuildTeamSuggestion(profile, g, mixes.TryGetValue(g.GroupId, out var mix)
                ? mix
                : new GroupRoleMixSnapshot(g.GroupId, 0, 0, 0)))
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

        return items;
    }

    public async Task<IReadOnlyList<TopicSuggestionDto>> SuggestTopicsForGroupAsync(
        Guid currentUserId,
        TopicSuggestionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var detail = await groupQueries.GetGroupAsync(request.GroupId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm.");

        var isMember = await groupQueries.IsActiveMemberAsync(request.GroupId, currentUserId, ct);
        if (!isMember)
            throw new UnauthorizedAccessException("Chỉ thành viên nhóm mới được xem gợi ý topic.");

        var semesterCtx = await ResolveSemesterAsync(detail.SemesterId, ct);
        EnsureWindow(DateOnly.FromDateTime(DateTime.UtcNow),
            semesterCtx.Policy.TopicSuggestStart,
            semesterCtx.Policy.TopicSelfSelectEnd,
            "Đã quá hạn tự chọn topic.");

        if (!detail.MajorId.HasValue)
            throw new InvalidOperationException("Nhóm chưa có chuyên ngành xác định.");

        var matches = await aiQueries.ListTopicMatchesAsync(request.GroupId, ct);
        if (matches.Count == 0)
            return Array.Empty<TopicSuggestionDto>();

        var available = await aiQueries.ListTopicAvailabilityAsync(detail.SemesterId, detail.MajorId, ct);
        var availableDict = available
            .Where(a => a.CanTakeMore)
            .ToDictionary(a => a.TopicId, a => a);

        if (availableDict.Count == 0)
            return Array.Empty<TopicSuggestionDto>();

        var limit = NormalizeLimit(request.Limit);
        var items = matches
            .Where(m => availableDict.TryGetValue(m.TopicId, out _))
            .Select(m =>
            {
                var bucket = availableDict[m.TopicId];
                var score = m.SimpleScore + 10;
                return new TopicSuggestionDto(m.TopicId, m.Title, m.Description, score, bucket.CanTakeMore);
            })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

        return items;
    }

    public async Task<AutoAssignTeamsResultDto> AutoAssignTeamsAsync(AutoAssignTeamsRequest request, CancellationToken ct)
    {
        request ??= new AutoAssignTeamsRequest(null, null, null);
        await aiQueries.RefreshStudentsPoolAsync(ct);
        var semesterCtx = await ResolveSemesterAsync(request.SemesterId, ct);
        var semesterId = semesterCtx.SemesterId;

        var students = await aiQueries.ListUnassignedStudentsAsync(semesterId, request.MajorId, ct);
        if (students.Count == 0)
            return new AutoAssignTeamsResultDto(0, Array.Empty<AutoAssignmentRecordDto>(), Array.Empty<Guid>(), Array.Empty<Guid>());

        var groups = await aiQueries.ListGroupCapacitiesAsync(semesterId, request.MajorId, ct);
        if (groups.Count == 0)
            return new AutoAssignTeamsResultDto(0, Array.Empty<AutoAssignmentRecordDto>(), students.Select(s => s.UserId).ToList(), Array.Empty<Guid>());

        var mixes = await aiQueries.GetGroupRoleMixAsync(groups.Select(g => g.GroupId), ct);
        var limit = request.Limit.HasValue && request.Limit.Value > 0 ? request.Limit.Value : int.MaxValue;

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

        foreach (var (majorId, groupStates) in groupsByMajor)
        {
            if (!studentsByMajor.TryGetValue(majorId, out var pools))
                continue;

            foreach (var groupState in groupStates)
            {
                while (groupState.RemainingSlots > 0 && limit > 0)
                {
                    var candidate = pools.DequeueForGroup(groupState);
                    if (candidate is null)
                        break;

                    await groupRepository.AddMembershipAsync(groupState.GroupId, candidate.UserId, groupState.SemesterId, "member", ct);
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

        var remainingStudentIds = studentsByMajor.Values
            .SelectMany(p => p.RemainingStudentIds)
            .Distinct()
            .ToList();

        var openGroups = groupsByMajor.Values
            .SelectMany(g => g)
            .Where(state => state.RemainingSlots > 0)
            .Select(state => state.GroupId)
            .Distinct()
            .ToList();

        return new AutoAssignTeamsResultDto(assignments.Count, assignments, remainingStudentIds, openGroups);
    }

    private static TeamSuggestionDto BuildTeamSuggestion(
        StudentProfileSnapshot student,
        GroupCapacitySnapshot group,
        GroupRoleMixSnapshot mix)
    {
        var role = AiRoleHelper.Parse(student.PrimaryRole);
        var needsFrontend = mix.FrontendCount == 0;
        var needsBackend = mix.BackendCount == 0;
        var roleBoost = role switch
        {
            AiPrimaryRole.Frontend when needsFrontend => 40,
            AiPrimaryRole.Backend when needsBackend => 40,
            AiPrimaryRole.Frontend => 15,
            AiPrimaryRole.Backend => 15,
            _ => 5
        };
        var capacityBoost = Math.Clamp(group.RemainingSlots, 1, 5) * 5;
        var completionBoost = student.SkillsCompleted ? 5 : 0;
        var score = roleBoost + capacityBoost + completionBoost;
        return new TeamSuggestionDto(group.GroupId, group.Name, group.Description, score, group.RemainingSlots, needsFrontend, needsBackend);
    }

    private async Task<(Guid SemesterId, SemesterPolicyDto Policy)> ResolveSemesterAsync(Guid? semesterId, CancellationToken ct)
    {
        SemesterDetailDto? detail = semesterId.HasValue
            ? await semesterQueries.GetByIdAsync(semesterId.Value, ct)
            : await semesterQueries.GetActiveAsync(ct);

        if (detail is null)
            throw new InvalidOperationException("Không tìm thấy học kỳ.");
        if (detail.Policy is null)
            throw new InvalidOperationException("Học kỳ chưa cấu hình policy.");

        return (detail.SemesterId, detail.Policy);
    }

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
