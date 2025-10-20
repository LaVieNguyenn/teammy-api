using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Common.Results;
using Teammy.Application.Mentors.ReadModels;

namespace Teammy.Application.Mentors;

public sealed class MentorService : IMentorService
{
    private readonly IMentorRepository _repo;
    public MentorService(IMentorRepository repo) => _repo = repo;

    public Task<PagedResult<OpenGroupReadModel>> ListOpenGroupsAsync(
        Guid termId, Guid? departmentId, string? topic, int page, int size, CancellationToken ct)
        => _repo.ListOpenGroupsAsync(termId, departmentId, topic, page, size, ct);

    public async Task<OperationResult> SelfAssignAsync(Guid groupId, Guid mentorId, CancellationToken ct)
    {
        if (mentorId == Guid.Empty) return OperationResult.Fail("Unauthorized", 401);

        var (topicId, termId) = await _repo.GetGroupTopicAndTermAsync(groupId, ct);
        if (topicId is null) return OperationResult.Fail("Group not found or has no topic", 404);

        const int maxGroups = 5; // policy demo
        var count = await _repo.CountAssignedTopicsInTermAsync(mentorId, termId!.Value, ct);
        if (count >= maxGroups) return OperationResult.Fail("Assignment limit reached for this term", 409);

        if (await _repo.ExistsMentorOnTopicAsync(topicId!.Value, mentorId, ct))
            return OperationResult.Success("Already mentor of this topic");

        var ok = await _repo.AddMentorToTopicAsync(topicId!.Value, mentorId, "owner", ct);
        return ok ? OperationResult.Success() : OperationResult.Fail("Cannot assign", 500);
    }

    public Task<bool> UnassignAsync(Guid groupId, Guid mentorId, CancellationToken ct)
        => _repo.RemoveMentorFromGroupAsync(groupId, mentorId, ct);

    public Task<IReadOnlyList<AssignedGroupReadModel>> GetAssignedGroupsAsync(Guid mentorId, CancellationToken ct)
        => _repo.GetAssignedGroupsAsync(mentorId, ct);

    public Task<MentorProfileReadModel?> GetMyProfileAsync(Guid mentorId, CancellationToken ct)
        => _repo.GetMentorProfileAsync(mentorId, ct);

    public Task<OperationResult> UpdateMyProfileAsync(Guid mentorId, string? bio, IEnumerable<string>? skills, IEnumerable<object>? availability, CancellationToken ct)
        => Task.FromResult(OperationResult.Fail("Requires mentor_profiles & user_skills tables", 501));
}
