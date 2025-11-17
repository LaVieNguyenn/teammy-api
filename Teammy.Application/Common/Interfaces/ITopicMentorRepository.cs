using System;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces;

public interface ITopicMentorRepository
{
    Task SetPrimaryMentorAsync(Guid topicId, Guid mentorId, CancellationToken ct);
    Task<Guid?> GetPrimaryMentorIdAsync(Guid topicId, CancellationToken ct);
}
