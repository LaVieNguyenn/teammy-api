namespace Teammy.Application.Common.Interfaces;

public interface IPositionReadOnlyQueries
{
    Task<Guid?> FindPositionIdByNameAsync(Guid majorId, string positionName, CancellationToken ct);
    Task<IReadOnlyList<(Guid PositionId, string PositionName)>> ListByMajorAsync(Guid majorId, CancellationToken ct);
}
