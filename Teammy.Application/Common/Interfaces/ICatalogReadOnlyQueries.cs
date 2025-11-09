using Teammy.Application.Catalog.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ICatalogReadOnlyQueries
{
    Task<SemesterDto?> GetActiveSemesterAsync(CancellationToken ct);
    Task<IReadOnlyList<SemesterDto>> ListSemestersAsync(CancellationToken ct);
    Task<IReadOnlyList<MajorDto>> ListMajorsAsync(CancellationToken ct);
    Task<IReadOnlyList<TopicDto>> ListTopicsAsync(CancellationToken ct);
}

