namespace Teammy.Application.Common.Interfaces;

public interface IMajorReadOnlyQueries
{
    Task<IReadOnlyList<string>> GetAllMajorNamesAsync(CancellationToken ct);
    Task<Guid?> FindMajorIdByNameAsync(string majorName, CancellationToken ct);
}
