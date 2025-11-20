using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Semesters.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class SemesterRepository : ISemesterRepository
{
    private readonly AppDbContext _db;

    public SemesterRepository(AppDbContext db)
    {
        _db = db;
    }
    public async Task<Guid> CreateAsync(
        string season,
        int year,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var entity = new semester
        {
            semester_id = Guid.NewGuid(),
            season      = season,
            year        = year,
            start_date  = startDate,
            end_date    = endDate,
            is_active   = false
        };

        _db.semesters.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.semester_id;
    }
    public async Task<bool> UpdateAsync(
        Guid semesterId,
        string season,
        int year,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var entity = await _db.semesters
            .SingleOrDefaultAsync(s => s.semester_id == semesterId, ct);

        if (entity is null) return false;

        entity.season     = season;
        entity.year       = year;
        entity.start_date = startDate;
        entity.end_date   = endDate;

        await _db.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> UpsertPolicyAsync(
        Guid semesterId,
        SemesterPolicyUpsertRequest req,
        CancellationToken ct)
    {
        var sem = await _db.semesters
            .Include(s => s.semester_policy)
            .SingleOrDefaultAsync(s => s.semester_id == semesterId, ct);

        if (sem is null) return false;

        var policy = sem.semester_policy;
        if (policy is null)
        {
            policy = new semester_policy
            {
                semester_id = sem.semester_id
            };
            _db.semester_policies.Add(policy);
            sem.semester_policy = policy;
        }

        policy.team_self_select_start  = req.TeamSelfSelectStart;
        policy.team_self_select_end    = req.TeamSelfSelectEnd;
        policy.team_suggest_start      = req.TeamSuggestStart;
        policy.topic_self_select_start = req.TopicSelfSelectStart;
        policy.topic_self_select_end   = req.TopicSelfSelectEnd;
        policy.topic_suggest_start     = req.TopicSuggestStart;
        policy.desired_group_size_min  = req.DesiredGroupSizeMin;
        policy.desired_group_size_max  = req.DesiredGroupSizeMax;

        await _db.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> ActivateAsync(Guid semesterId, CancellationToken ct)
    {
        var target = await _db.semesters
            .SingleOrDefaultAsync(s => s.semester_id == semesterId, ct);

        if (target is null) return false;

        var actives = await _db.semesters
            .Where(s => s.is_active)
            .ToListAsync(ct);

        foreach (var s in actives)
        {
            s.is_active = false;
        }

        target.is_active = true;
        await _db.SaveChangesAsync(ct);

        return true;
    }
}
