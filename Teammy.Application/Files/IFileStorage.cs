namespace Teammy.Application.Files;

public interface IFileStorage
{
    Task<(string fileUrl, string? fileType, long? fileSize)> SaveAsync(Stream content, string fileName, CancellationToken ct);
    Task DeleteAsync(string fileUrl, CancellationToken ct);
}
