namespace Teammy.Application.Common.Interfaces;

public interface IMajorReadOnlyQueries
{
    Task<IReadOnlyList<string>> GetAllMajorNamesAsync(CancellationToken ct);
    Task<Guid?> FindMajorIdByNameAsync(string majorName, CancellationToken ct);

    Task<IReadOnlyList<(Guid MajorId, string MajorName)>> ListAsync(CancellationToken ct);
    Task<(Guid MajorId, string MajorName)?> GetAsync(Guid id, CancellationToken ct);
}
