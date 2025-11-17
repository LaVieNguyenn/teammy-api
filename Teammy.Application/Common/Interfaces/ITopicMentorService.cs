using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces
{
    public interface ITopicMentorService
    {
        Task ReplaceMentorsAsync(Guid topicId, IReadOnlyList<Guid> mentorIds, CancellationToken ct);
    }
}
