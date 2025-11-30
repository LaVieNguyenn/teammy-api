namespace Teammy.Application.Ai.Dtos;

public sealed record RecruitmentPostSuggestionRequest(Guid? MajorId, int? Limit);

public sealed record TopicSuggestionRequest(Guid GroupId, int? Limit);
public sealed record ProfilePostSuggestionRequest(Guid GroupId, int? Limit);

public sealed record AutoAssignTeamsRequest(Guid? MajorId, int? Limit);
public sealed record AutoAssignTopicRequest(Guid? GroupId, Guid? MajorId, int? LimitPerGroup);
