using System.Text.RegularExpressions;

namespace Teammy.Application.Common.Utils;

public static class SemesterCode
{
    public static (string Season, int Year) Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Semester code is required");

        var s = raw.Trim().ToUpperInvariant();
        s = Regex.Replace(s, @"[\s_\-]+", "");

        s = s.Replace("AUTUMN", "FALL");
        s = s.Replace("FA", "FALL");
        s = s.Replace("SP", "SPRING");
        s = s.Replace("SU", "SUMMER");

        var m4 = Regex.Match(s, @"^(SPRING|SUMMER|FALL)(20\d{2})$");
        if (m4.Success) return (m4.Groups[1].Value, int.Parse(m4.Groups[2].Value));

        var m2 = Regex.Match(s, @"^(SPRING|SUMMER|FALL)(\d{2})$");
        if (m2.Success) return (m2.Groups[1].Value, 2000 + int.Parse(m2.Groups[2].Value));

        throw new ArgumentException($"Cannot parse semester code: '{raw}'");
    }
}
