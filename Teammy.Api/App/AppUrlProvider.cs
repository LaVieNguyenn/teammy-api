using Microsoft.Extensions.Configuration;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.App;

public sealed class AppUrlProvider(IConfiguration cfg) : IAppUrlProvider
{
    public string GetInvitationUrl(Guid invitationId, Guid groupId)
    {
        var baseUrl = cfg["App:ClientUrl"]?.TrimEnd('/') ?? "https://teammy.vercel.app";
        return $"{baseUrl}/login";
    }
}

