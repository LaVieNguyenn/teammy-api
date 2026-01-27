using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Teammy.Application.Auth.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Api.Controllers;

/// <summary>
/// Dev-only username/password login.
/// - Accounts are fixed in appsettings (DevAuth:Users)
/// - Only enabled in Development and when DevAuth:Enabled=true
/// </summary>
[ApiController]
[Route("api/auth/dev")]
public sealed class DevAuthController(
    IWebHostEnvironment env,
    IConfiguration cfg,
    AppDbContext db,
    ITokenService tokenService) : ControllerBase
{
    public sealed record DevLoginRequest(string Username, string Password);

    public sealed record DevAuthUser(string Role, string Username, string Password);

    private sealed record DevAuthOptions(
        bool Enabled,
        string? EmailDomain,
        IReadOnlyList<DevAuthUser> Users);

    private DevAuthOptions GetOptions()
    {
        var enabled = cfg.GetValue<bool?>("DevAuth:Enabled") ?? false;
        var domain = cfg["DevAuth:EmailDomain"];
        var users = cfg.GetSection("DevAuth:Users").Get<List<DevAuthUser>>() ?? new List<DevAuthUser>();

        // Normalize and drop invalid rows.
        users = users
            .Where(u => u is not null
                        && !string.IsNullOrWhiteSpace(u.Role)
                        && !string.IsNullOrWhiteSpace(u.Username)
                        && !string.IsNullOrWhiteSpace(u.Password))
            .Select(u => new DevAuthUser(
                Role: NormalizeRole(u.Role),
                Username: (u.Username ?? string.Empty).Trim(),
                Password: u.Password ?? string.Empty))
            .ToList();

        return new DevAuthOptions(
            Enabled: enabled,
            EmailDomain: domain,
            Users: users);
    }

    private bool IsDevEnabled(out DevAuthOptions opts)
    {
        opts = GetOptions();
        return env.IsDevelopment() && opts.Enabled && opts.Users.Count > 0;
    }

    private IActionResult DevNotAvailable()
        => NotFound();

    private static string NormalizeRole(string role)
        => (role ?? string.Empty).Trim().ToLowerInvariant();

    private static string ComputeEmail(DevAuthOptions opts, string username)
    {
        var domain = string.IsNullOrWhiteSpace(opts.EmailDomain) ? "dev.teammy.local" : opts.EmailDomain!.Trim();
        return $"{username}@{domain}";
    }

    private static string ComputeDisplayName(string roleName)
    {
        var ti = CultureInfo.InvariantCulture.TextInfo;
        var nice = ti.ToTitleCase(NormalizeRole(roleName));
        return $"Dev {nice}";
    }

    private async Task EnsureConfiguredDevUsersAsync(DevAuthOptions opts, CancellationToken ct)
    {
        var roles = await db.roles.AsNoTracking()
            .Select(r => new { r.role_id, name = r.name.ToLower() })
            .ToListAsync(ct);

        foreach (var u in opts.Users)
        {
            var roleName = NormalizeRole(u.Role);
            if (string.IsNullOrWhiteSpace(roleName))
                continue;

            // Map common plural mistakes.
            if (roleName == "students") roleName = "student";

            var roleRow = roles.FirstOrDefault(r => r.name == roleName);
            if (roleRow is null)
                continue; // skip unknown role in this environment

            var email = ComputeEmail(opts, u.Username);
            var displayName = ComputeDisplayName(roleName);
            await EnsureUserForRoleAsync(roleRow.role_id, roleName, email, displayName, ct);
        }
    }

    private async Task<user> EnsureUserForRoleAsync(Guid roleId, string roleName, string email, string displayName, CancellationToken ct)
    {
        var existing = await db.users
            .FirstOrDefaultAsync(u => u.email == email, ct);

        if (existing is null)
        {
            existing = new user
            {
                user_id = Guid.NewGuid(),
                email = email,
                email_verified = true,
                display_name = displayName,
                avatar_url = null,
                phone = null,
                student_code = null,
                gender = null,
                major_id = null,
                gpa = null,
                desired_position_id = null,
                portfolio_url = null,
                skills = null,
                skills_completed = true,
                is_active = true,
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };

            db.users.Add(existing);
        }
        else
        {
            if (!existing.is_active) existing.is_active = true;
            if (!string.Equals(existing.display_name, displayName, StringComparison.Ordinal))
                existing.display_name = displayName;
            if (!existing.email_verified) existing.email_verified = true;
            existing.updated_at = DateTime.UtcNow;
        }

        // Ensure exactly 1 system role mapping for this dev user.
        var currentRoles = await db.user_roles
            .Where(ur => ur.user_id == existing.user_id)
            .ToListAsync(ct);

        if (currentRoles.Count != 1 || currentRoles[0].role_id != roleId)
        {
            if (currentRoles.Count > 0)
                db.user_roles.RemoveRange(currentRoles);

            db.user_roles.Add(new user_role
            {
                user_role_id = Guid.NewGuid(),
                user_id = existing.user_id,
                role_id = roleId
            });
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }

    [HttpPost("seed")]
    [AllowAnonymous]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        if (!IsDevEnabled(out var opts))
            return DevNotAvailable();

        await EnsureConfiguredDevUsersAsync(opts, ct);

        var accounts = opts.Users
            .Select(u => new
            {
                role = NormalizeRole(u.Role) == "students" ? "student" : NormalizeRole(u.Role),
                username = u.Username,
                email = ComputeEmail(opts, u.Username)
            })
            .OrderBy(x => x.role)
            .ToList();

        return Ok(new { ok = true, accounts });
    }

    [HttpGet("accounts")]
    [AllowAnonymous]
    public IActionResult Accounts(CancellationToken ct)
    {
        if (!IsDevEnabled(out var opts))
            return DevNotAvailable();

        var accounts = opts.Users
            .Select(u => new
            {
                role = NormalizeRole(u.Role) == "students" ? "student" : NormalizeRole(u.Role),
                username = u.Username,
                email = ComputeEmail(opts, u.Username)
            })
            .OrderBy(x => x.role)
            .ToList();

        return Ok(new { ok = true, accounts });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] DevLoginRequest request, CancellationToken ct)
    {
        if (!IsDevEnabled(out var opts))
            return NotFound();

        var username = (request.Username ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("username is required");

        var match = opts.Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return Unauthorized("unknown dev account");

        if (!string.Equals(password, match.Password, StringComparison.Ordinal))
            return Unauthorized("invalid credentials");

        // Ensure configured users exist.
        await EnsureConfiguredDevUsersAsync(opts, ct);

        var roleName = NormalizeRole(match.Role);
        if (roleName == "students") roleName = "student";

        var roleRow = await db.roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.name.ToLower() == roleName, ct);

        if (roleRow is null)
            return Unauthorized("unknown role");

        var email = ComputeEmail(opts, match.Username);
        var displayName = ComputeDisplayName(roleName);

        var devUser = await EnsureUserForRoleAsync(roleRow.role_id, roleName, email, displayName, ct);

        var jwt = tokenService.CreateAccessToken(
            devUser.user_id,
            devUser.email,
            devUser.display_name,
            roleName,
            semester: null);

        return Ok(new LoginResponse(jwt, devUser.user_id, devUser.email, devUser.display_name, roleName, Semester: null));
    }
}
