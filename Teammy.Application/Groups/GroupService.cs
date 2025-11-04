using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Common.Results;
using Teammy.Application.Groups.ReadModels;

namespace Teammy.Application.Groups;

public sealed class GroupService : IGroupService
{
    private readonly IGroupRepository _repo;
    public GroupService(IGroupRepository repo) => _repo = repo;

    public async Task<OperationResult> CreateAsync(Guid termId, string name, int capacity, Guid? topicId, string? description, string? techStack, string? githubUrl, Guid creatorUserId, CancellationToken ct)
    {
        if (creatorUserId == Guid.Empty) return OperationResult.Fail("Unauthorized", 401);
        if (termId == Guid.Empty) return OperationResult.Fail("TERM_ID_REQUIRED", 400);
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("NAME_REQUIRED", 400);
        if (capacity < 1) return OperationResult.Fail("CAPACITY_INVALID", 400);

        if (await _repo.UserHasActiveGroupInTermAsync(creatorUserId, termId, ct))
            return OperationResult.Fail("USER_ALREADY_IN_GROUP", 409);

        var id = await _repo.CreateGroupAsync(termId, name.Trim(), capacity, topicId, description, techStack, githubUrl, creatorUserId, ct);
        return OperationResult.Success(id.ToString());
    }

    public Task<GroupReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
        => _repo.GetByIdAsync(id, ct);

    public Task<PagedResult<GroupReadModel>> ListOpenAsync(Guid termId, Guid? topicId, Guid? departmentId, Guid? majorId, string? q, int page, int size, CancellationToken ct)
        => _repo.ListOpenAsync(termId, topicId, departmentId, majorId, q, page, size, ct);

    public async Task<OperationResult> JoinAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var g = await _repo.GetByIdAsync(groupId, ct);
        if (g is null) return OperationResult.Fail("GROUP_NOT_FOUND", 404);
        if (await _repo.UserHasActiveGroupInTermAsync(userId, g.TermId, ct))
            return OperationResult.Fail("USER_ALREADY_IN_GROUP", 409);

        var ok = await _repo.AddJoinRequestAsync(groupId, userId, g.TermId, ct);
        return ok ? OperationResult.Success() : OperationResult.Fail("CANNOT_JOIN", 400);
    }

    public async Task<OperationResult> LeaveAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var (ok, reason) = await _repo.LeaveAsync(groupId, userId, ct);
        return ok ? OperationResult.Success() : OperationResult.Fail(reason ?? "CANNOT_LEAVE", 409);
    }
    public async Task<IReadOnlyList<PendingMemberReadModel>> GetPendingMembersAsync(Guid groupId, Guid leaderId, CancellationToken ct)
    {
        if (!await _repo.IsLeaderAsync(groupId, leaderId, ct)) return Array.Empty<PendingMemberReadModel>();
        return await _repo.GetPendingMembersAsync(groupId, ct);
    }

    public async Task<OperationResult> AcceptAsync(Guid groupId, Guid leaderId, Guid userId, CancellationToken ct)
    {
        if (!await _repo.IsLeaderAsync(groupId, leaderId, ct)) return OperationResult.Fail("NOT_LEADER", 403);
        var g = await _repo.GetByIdAsync(groupId, ct);
        if (g is null) return OperationResult.Fail("GROUP_NOT_FOUND", 404);
        if (g.Members >= g.Capacity) return OperationResult.Fail("GROUP_FULL", 409);
        var (ok, reason) = await _repo.AcceptPendingAsync(groupId, userId, ct);
        return ok ? OperationResult.Success() : OperationResult.Fail(reason ?? "CANNOT_ACCEPT", 409);
    }

    public async Task<OperationResult> RejectAsync(Guid groupId, Guid leaderId, Guid userId, CancellationToken ct)
    {
        if (!await _repo.IsLeaderAsync(groupId, leaderId, ct)) return OperationResult.Fail("NOT_LEADER", 403);
        var (ok, reason) = await _repo.RejectPendingAsync(groupId, userId, ct);
        return ok ? OperationResult.Success() : OperationResult.Fail(reason ?? "CANNOT_REJECT", 409);
    }
}

