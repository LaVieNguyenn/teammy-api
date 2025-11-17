using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Topics
{
    public sealed class TopicMentorService : ITopicMentorService
    {
        private readonly AppDbContext _db;

        public TopicMentorService(AppDbContext db)
        {
            _db = db;
        }

        public async Task ReplaceMentorsAsync(Guid topicId, IReadOnlyList<Guid> mentorIds, CancellationToken ct)
        {
            var topic = await _db.topics
                .Include(t => t.mentors)
                .FirstOrDefaultAsync(t => t.topic_id == topicId, ct)
                ?? throw new InvalidOperationException("Topic not found.");

            topic.mentors.Clear();

            if (mentorIds.Count > 0)
            {
                var distinctIds = mentorIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (distinctIds.Count > 0)
                {
                    var mentors = await _db.users
                        .Where(u => distinctIds.Contains(u.user_id) && u.is_active)
                        .ToListAsync(ct);

                    foreach (var m in mentors)
                        topic.mentors.Add(m);
                }
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
