namespace Teammy.Application.Ai.Models;

public enum AiPrimaryRole
{
    Unknown = 0,
    Frontend = 1,
    Backend = 2,
    Other = 3
}

public static class AiRoleHelper
{
    public static AiPrimaryRole Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AiPrimaryRole.Unknown;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "fe" or "front" or "front-end" or "frontend" => AiPrimaryRole.Frontend,
            "be" or "back" or "back-end" or "backend" => AiPrimaryRole.Backend,
            _ => AiPrimaryRole.Other
        };
    }

    public static string ToDisplayString(AiPrimaryRole role) => role switch
    {
        AiPrimaryRole.Frontend => "frontend",
        AiPrimaryRole.Backend => "backend",
        AiPrimaryRole.Other => "other",
        _ => "unknown"
    };
}
