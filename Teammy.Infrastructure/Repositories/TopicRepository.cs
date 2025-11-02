using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces.Persistence;
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
    }
}
