using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces;

public interface IAiSemanticSearch
{
    Task<IReadOnlyList<Guid>> SearchIdsAsync(
        string queryText,
        string type,
        Guid semesterId,
        Guid? majorId,
        int limit,
        CancellationToken ct);
}
