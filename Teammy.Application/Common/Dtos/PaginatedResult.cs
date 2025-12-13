using System.Collections.Generic;

namespace Teammy.Application.Common.Dtos;

public sealed record PaginatedResult<T>(int TotalCount, int Page, int PageSize, IReadOnlyList<T> Items);
