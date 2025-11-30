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
    private static readonly string[] FrontendKeywords =
    {
        "frontend","front-end","ui","ux","react","vue","angular","svelte","css","html",
        "tailwind","bootstrap","figma","design","nextjs","nuxt","web"
    };

    private static readonly string[] BackendKeywords =
    {
        "backend","back-end","api","server","database","sql","rest","graphql","java",
        "spring","python","django","flask","dotnet","aspnet","node","express","go","golang",
        "kafka","microservice","cloud","aws","azure"
    };

    private static readonly string[] MobileKeywords =
    {
        "mobile","android","ios","swift","kotlin","flutter","reactnative","react-native",
        "xamarin","ionic"
    };

    private static readonly string[] GeneralTechKeywords =
    {
        "devops","data","machinelearning","ml","ai","ar","vr","unity","unreal"
    };

    public static AiPrimaryRole Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AiPrimaryRole.Unknown;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "fe" or "front" or "front-end" or "frontend" or "ui" or "ux" => AiPrimaryRole.Frontend,
            "be" or "back" or "back-end" or "backend" or "server" or "api" => AiPrimaryRole.Backend,
            "mobile" or "android" or "ios" or "flutter" or "swift" or "kotlin" => AiPrimaryRole.Other,
            _ => AiPrimaryRole.Unknown
        };
    }

    public static AiPrimaryRole InferFromTags(IEnumerable<string> rawTags)
    {
        if (rawTags is null)
            return AiPrimaryRole.Unknown;

        var score = new Dictionary<AiPrimaryRole, int>
        {
            [AiPrimaryRole.Frontend] = 0,
            [AiPrimaryRole.Backend] = 0,
            [AiPrimaryRole.Other] = 0
        };

        foreach (var tag in rawTags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            var token = tag.Trim().ToLowerInvariant();
            if (MatchesAny(token, FrontendKeywords))
                score[AiPrimaryRole.Frontend] += 2;
            if (MatchesAny(token, BackendKeywords))
                score[AiPrimaryRole.Backend] += 2;
            if (MatchesAny(token, MobileKeywords))
                score[AiPrimaryRole.Other] += 2;

            if (MatchesAny(token, GeneralTechKeywords))
                score[AiPrimaryRole.Other] += 1;
        }

        var best = score
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault(kvp => kvp.Value > 0);

        return best.Value > 0 ? best.Key : AiPrimaryRole.Unknown;
    }

    private static bool MatchesAny(string token, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (token.Contains(keyword))
                return true;
        }

        return false;
    }

    public static string ToDisplayString(AiPrimaryRole role) => role switch
    {
        AiPrimaryRole.Frontend => "frontend",
        AiPrimaryRole.Backend => "backend",
        AiPrimaryRole.Other => "other",
        _ => "unknown"
    };
}
