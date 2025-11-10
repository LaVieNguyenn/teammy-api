namespace Teammy.Application.Common.Interfaces;

public interface ISemesterWriteRepository
{
    Task<Guid> EnsureByCodeAsync(string anySemesterText, CancellationToken ct);
}
