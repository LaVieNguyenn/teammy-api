using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Infrastructure.Auth;

public sealed class JwtTokenService(IConfiguration cfg) : ITokenService
{
    public string CreateAccessToken(Guid userId, string email, string displayName, string role, TokenSemesterInfo? semester)
    {
        var issuer   = cfg["Auth:Jwt:Issuer"]!;
        var audience = cfg["Auth:Jwt:Audience"]!;
        var key      = cfg["Auth:Jwt:Key"]!;

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(ClaimTypes.Role, role),
        };
        if (semester is not null)
        {
            claims.Add(new Claim("semester_id", semester.SemesterId.ToString()));
            claims.Add(new Claim("semester_season", semester.Season));
            claims.Add(new Claim("semester_year", semester.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            claims.Add(new Claim("semester_start", semester.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            claims.Add(new Claim("semester_end", semester.EndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
