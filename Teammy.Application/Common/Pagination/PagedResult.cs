using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Pagination
{
    public sealed record PagedResult<T>(int Total, int Page, int Size, IReadOnlyList<T> Items);
}
