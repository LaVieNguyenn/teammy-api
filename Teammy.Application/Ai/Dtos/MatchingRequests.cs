namespace Teammy.Application.Ai.Dtos;

public sealed record RecruitmentPostSuggestionRequest(Guid? SemesterId, Guid? MajorId, int? Limit);

public sealed record TopicSuggestionRequest(Guid GroupId, int? Limit);
public sealed record ProfilePostSuggestionRequest(Guid GroupId, int? Limit);

public sealed record AutoAssignTeamsRequest(Guid? SemesterId, Guid? MajorId, int? Limit);
public sealed record AutoAssignTopicRequest(Guid? GroupId, Guid? SemesterId, Guid? MajorId, int? LimitPerGroup);
