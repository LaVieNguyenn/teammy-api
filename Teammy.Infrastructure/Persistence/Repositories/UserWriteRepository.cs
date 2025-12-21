using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class UserWriteRepository : IUserWriteRepository
{
    private readonly AppDbContext _db;

    public UserWriteRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> EmailExistsAnyAsync(string email, CancellationToken ct)
        => _db.users.AsNoTracking().AnyAsync(u => u.email.ToLower() == email.ToLower(), ct);
    public Task<bool> DisplayNameExistsAnyAsync(string displayName, CancellationToken ct)
          => _db.users.AsNoTracking().AnyAsync(u => u.display_name.ToLower() == displayName.ToLower(), ct);

    public Task<bool> StudentCodeExistsAnyAsync(string studentCode, CancellationToken ct)
        => _db.users.AsNoTracking().AnyAsync(u => u.student_code == studentCode, ct);
    public async Task<Guid> CreateUserAsync(
        string email,
        string displayName,
        string? studentCode,
        string? gender,
        Guid? majorId,
        CancellationToken ct)
    {
        var entity = new user
        {
            user_id = Guid.NewGuid(),
            email = email,
            email_verified = true,
            display_name = displayName,
            avatar_url = null,
            phone = null,
            student_code = studentCode,
            gender = gender,
            major_id = majorId,
            portfolio_url = null,
            skills = null,
            skills_completed = false,
            is_active = true,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };

        _db.users.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.user_id;
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct)
    {
        var exists = await _db.user_roles
            .AnyAsync(x => x.user_id == userId && x.role_id == roleId, ct);
        if (exists) return;

        var linking = new user_role
        {
            user_role_id = Guid.NewGuid(),
            user_id = userId,
            role_id = roleId,
        };
        _db.user_roles.Add(linking);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateUserAsync(
        Guid userId,
        string displayName,
        string? studentCode,
        string? gender,
        Guid? majorId,
        bool isActive,
        string? portfolioUrl,
        CancellationToken ct)
    {
        var entity = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId, ct);
        if (entity is null)
            throw new KeyNotFoundException("User not found");

        entity.display_name = displayName;
        entity.student_code = studentCode;
        entity.gender = gender;
        entity.major_id = majorId;
        entity.is_active = isActive;
        entity.portfolio_url = NormalizePortfolio(portfolioUrl);
        entity.updated_at = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        var entity = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId, ct);
        if (entity is null) return;

        entity.is_active = false;
        entity.updated_at = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetSingleRoleAsync(Guid userId, Guid roleId, CancellationToken ct)
    {
        var existing = _db.user_roles.Where(x => x.user_id == userId);
        _db.user_roles.RemoveRange(existing);

        var linking = new user_role
        {
            user_role_id = Guid.NewGuid(),
            user_id = userId,
            role_id = roleId
        };

        _db.user_roles.Add(linking);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateProfileAsync(
        Guid userId,
        string displayName,
        string? phone,
        string? studentCode,
        string? gender,
        Guid? majorId,
        string? skillsJson,
        bool skillsCompleted,
        string? portfolioUrl,
        CancellationToken ct)
    {
        var entity = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId, ct);
        if (entity is null)
            throw new KeyNotFoundException("User not found");

        entity.display_name = displayName;
        entity.phone = phone;
        entity.student_code = studentCode;
        entity.gender = gender;
        entity.major_id = majorId;
        entity.skills = skillsJson;
        entity.skills_completed = skillsCompleted;
        entity.portfolio_url = NormalizePortfolio(portfolioUrl);
        entity.updated_at = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAvatarAsync(Guid userId, string avatarUrl, CancellationToken ct)
    {
        var entity = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId, ct);
        if (entity is null)
            throw new KeyNotFoundException("User not found");

        entity.avatar_url = avatarUrl;
        entity.updated_at = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private static string? NormalizePortfolio(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
