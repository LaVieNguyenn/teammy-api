namespace Teammy.Application.Common.Interfaces;

public interface IStudentSemesterReadOnlyQueries
{
    Task<Guid?> GetCurrentSemesterIdAsync(Guid userId, CancellationToken ct);
}
