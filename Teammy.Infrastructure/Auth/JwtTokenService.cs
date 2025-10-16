using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Teammy.Application.Common.Interfaces.Auth;

namespace Teammy.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    private readonly IConfiguration _cfg;
    public JwtTokenService(IConfiguration cfg) => _cfg = cfg;

    public string CreateAccessToken(Guid userId, string email, string displayName, string role, string? picture)
    {
        var issuer   = _cfg["Auth:Jwt:Issuer"]!;
        var audience = _cfg["Auth:Jwt:Audience"]!;
        var key      = _cfg["Auth:Jwt:Key"]!;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("uid", userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("name", displayName),
            new Claim("picture", picture ?? string.Empty)
        };

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                                           SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer, audience: audience, claims: claims,
            notBefore: DateTime.UtcNow,
            expires:   DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
