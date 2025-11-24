using System.Collections.Generic;

namespace Teammy.Application.Skills.Dtos;

public sealed record SkillDictionaryDto(
    string Token,
    string Role,
    string Major,
    IReadOnlyList<string> Aliases
);

public sealed record CreateSkillDictionaryRequest(
    string Token,
    string Role,
    string Major,
    IReadOnlyList<string>? Aliases
);

public sealed record UpdateSkillDictionaryRequest(
    string Role,
    string Major,
    IReadOnlyList<string>? Aliases
);
