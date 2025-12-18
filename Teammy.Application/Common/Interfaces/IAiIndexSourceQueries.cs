using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces;

public interface IAiIndexSourceQueries
{
    Task<TopicIndexRow?> GetTopicAsync(Guid topicId, CancellationToken ct);
    Task<RecruitmentPostIndexRow?> GetRecruitmentPostAsync(Guid postId, CancellationToken ct);
    Task<ProfilePostIndexRow?> GetProfilePostAsync(Guid postId, CancellationToken ct);
}

public sealed record TopicIndexRow(
    Guid TopicId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    IReadOnlyList<string> SkillNames,
    string? SkillsJson);

public sealed record RecruitmentPostIndexRow(
    Guid PostId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    string? MajorName,
    string? GroupName,
    string? PositionNeeded,
    string? RequiredSkills);

public sealed record ProfilePostIndexRow(
    Guid PostId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    string? OwnerDisplayName,
    string? SkillsJson,
    string? SkillsText,
    string? PrimaryRole);
