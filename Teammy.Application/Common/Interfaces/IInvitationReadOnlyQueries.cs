using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationReadOnlyQueries
{
    Task<InvitationDetailDto?> GetAsync(Guid invitationId, CancellationToken ct);
    Task<IReadOnlyList<InvitationListItemDto>> ListForUserAsync(Guid userId, string? status, CancellationToken ct);
}

