using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Topics.ReadModels;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Repositories
{
    public sealed class TopicRepository : ITopicRepository
    {
        private readonly AppDbContext _db;
        public TopicRepository(AppDbContext db) => _db = db;

        public async Task<TopicReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            var e = await _db.topics.AsNoTracking().FirstOrDefaultAsync(t => t.id == id, ct);
            if (e is null) return null;
            return new TopicReadModel
            {
                Id = e.id,
                TermId = e.term_id,
                Code = e.code,
                Title = e.title,
                Description = e.description,
                DepartmentId = e.department_id,
                MajorId = e.major_id,
                Status = e.status,
                CreatedAt = e.created_at
            };
        }

        public Task<bool> ExistsTitleInTermAsync(Guid termId, string title, Guid excludeId, CancellationToken ct)
        {
            return _db.topics.AsNoTracking().AnyAsync(
                t => t.term_id == termId && t.id != excludeId && t.title.ToLower() == title.ToLower(), ct);
        }

        public async Task<bool> UpdateAsync(Guid id, string? title, string? code, string? description, Guid? departmentId, Guid? majorId, CancellationToken ct)
        {
            var e = await _db.topics.FirstOrDefaultAsync(t => t.id == id, ct);
            if (e is null) return false;
            if (title is not null) e.title = title;
            if (code is not null) e.code = string.IsNullOrWhiteSpace(code) ? null : code;
            if (description is not null) e.description = string.IsNullOrWhiteSpace(description) ? null : description;
            if (departmentId.HasValue) e.department_id = departmentId.Value;
            if (majorId.HasValue) e.major_id = majorId.Value;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ArchiveAsync(Guid id, CancellationToken ct)
        {
            var e = await _db.topics.FirstOrDefaultAsync(t => t.id == id, ct);
            if (e is null) return false;
            e.status = "archived";
            await _db.SaveChangesAsync(ct);
            return true;
        }
        public async Task<PagedResult<TopicReadModel>> SearchAsync(Guid termId, string? status, Guid? departmentId, Guid? majorId, string? q, string? sort, int page, int size, CancellationToken ct)
        {
            if (page < 1) page = 1;
            if (size < 1) size = 20; else if (size > 200) size = 200;

            var query = _db.topics.AsNoTracking().Where(t => t.term_id == termId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = status.Trim().ToLower();
                query = query.Where(t => t.status.ToLower() == s);
            }
            if (departmentId.HasValue)
                query = query.Where(t => t.department_id == departmentId);
            if (majorId.HasValue)
                query = query.Where(t => t.major_id == majorId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var ql = q.Trim().ToLower();
                query = query.Where(t => t.title.ToLower().Contains(ql) || (t.code != null && t.code.ToLower().Contains(ql)));
            }

            // sorting
            bool desc = true; 
            string by = "created_at";
            if (!string.IsNullOrWhiteSpace(sort))
            {
                var s = sort.Trim();
                desc = s.StartsWith("-");
                by = desc ? s.Substring(1) : s;
            }

            IOrderedQueryable<Teammy.Infrastructure.Models.topic> ordered;
            switch (by)
            {
                case "title":
                    ordered = desc ? query.OrderByDescending(t => t.title) : query.OrderBy(t => t.title);
                    break;
                default:
                    ordered = desc ? query.OrderByDescending(t => t.created_at) : query.OrderBy(t => t.created_at);
                    break;
            }

            var total = await query.CountAsync(ct);
            var items = await ordered
                .Skip((page - 1) * size)
                .Take(size)
                .Select(e => new TopicReadModel
                {
                    Id = e.id,
                    TermId = e.term_id,
                    Code = e.code,
                    Title = e.title,
                    Description = e.description,
                    DepartmentId = e.department_id,
                    MajorId = e.major_id,
                    Status = e.status,
                    CreatedAt = e.created_at
                })
                .ToListAsync(ct);

            return new PagedResult<TopicReadModel>(total, page, size, items);
        }

    }
}
