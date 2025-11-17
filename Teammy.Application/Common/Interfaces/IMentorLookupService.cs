using System;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces
{
    public interface IMentorLookupService
    {
        Task<Guid> GetMentorIdByEmailAsync(string email, CancellationToken ct);
    }
}
