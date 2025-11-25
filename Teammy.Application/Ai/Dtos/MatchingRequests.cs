namespace Teammy.Application.Ai.Dtos;

public sealed record TeamSuggestionRequest(Guid? SemesterId, int? Limit);

public sealed record TopicSuggestionRequest(Guid GroupId, int? Limit);

public sealed record AutoAssignTeamsRequest(Guid? SemesterId, Guid? MajorId, int? Limit);
