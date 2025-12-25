namespace Teammy.Application.Common.Interfaces;

public interface IStudentSemesterWriteRepository
{
    Task SetCurrentSemesterAsync(Guid userId, Guid semesterId, CancellationToken ct);
}
