namespace Teammy.Application.Ai.Dtos;

public sealed record RecruitmentPostSuggestionRequest(Guid? MajorId, int? Limit);

public sealed record TopicSuggestionRequest(Guid GroupId, int? Limit);
public sealed record ProfilePostSuggestionRequest(Guid GroupId, int? Limit);

public sealed record AutoAssignTeamsRequest(Guid? SemesterId, Guid? MajorId, int? Limit);
public sealed record AutoAssignTopicRequest(Guid? GroupId, Guid? MajorId, int? LimitPerGroup, Guid? SemesterId = null);

public sealed record AiOptionRequest(
	Guid? SemesterId,
	AiOptionSection Section = AiOptionSection.All,
	int Page = 1,
	int PageSize = 20);

public sealed record AiAutoResolveRequest(Guid? SemesterId, Guid? MajorId);
