namespace Teammy.Application.Common.Interfaces;

public interface IPositionWriteRepository
{
    Task<Guid> CreateAsync(Guid majorId, string positionName, CancellationToken ct);
    Task UpdateAsync(Guid positionId, Guid majorId, string positionName, CancellationToken ct);
    Task DeleteAsync(Guid positionId, CancellationToken ct);
}
