namespace Teammy.Application.Common.Interfaces;

public interface IMajorWriteRepository
{
    Task<Guid> CreateAsync(string name, CancellationToken ct);
    Task UpdateAsync(Guid id, string name, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

