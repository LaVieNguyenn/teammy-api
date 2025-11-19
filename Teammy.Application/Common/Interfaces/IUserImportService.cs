using Teammy.Application.Users.Import.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IUserImportService
{
    Task<byte[]> BuildTemplateAsync(CancellationToken ct);

    Task<ImportUsersResult> ImportAsync(Stream excelStream, Guid performedByUserId, CancellationToken ct);

    Task<UserImportValidationResult> ValidateRowsAsync(
        IReadOnlyList<UserImportPayloadRow> rows,
        CancellationToken ct);
}
