using Microsoft.Extensions.Configuration;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.App;

public sealed class AppUrlProvider(IConfiguration cfg) : IAppUrlProvider
{
    public string GetInvitationUrl(Guid invitationId, Guid groupId)
    {
        var baseUrl = cfg["App:ClientUrl"]?.TrimEnd('/') ?? "https://teammy.info.vn";
        // Frontend route suggestion: /invitations/{id}?groupId=...
        return $"{baseUrl}/invitations/{invitationId}?groupId={groupId}";
    }
}

