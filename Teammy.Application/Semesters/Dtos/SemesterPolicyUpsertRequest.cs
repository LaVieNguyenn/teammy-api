namespace Teammy.Application.Semesters.Dtos;

public sealed record SemesterPolicyUpsertRequest(
    DateOnly TeamSelfSelectStart,
    DateOnly TeamSelfSelectEnd,
    DateOnly TeamSuggestStart,
    DateOnly TopicSelfSelectStart,
    DateOnly TopicSelfSelectEnd,
    DateOnly TopicSuggestStart,
    int DesiredGroupSizeMin,
    int DesiredGroupSizeMax
);
