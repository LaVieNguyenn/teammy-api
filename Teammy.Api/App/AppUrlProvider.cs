using Microsoft.Extensions.Configuration;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.App;

public sealed class AppUrlProvider(IConfiguration cfg) : IAppUrlProvider
{
    private readonly string _baseUrl = cfg["App:ClientUrl"]?.TrimEnd('/') ?? "https://teammy.vercel.app";

    public string GetInvitationUrl(Guid invitationId, Guid groupId)
        => $"{_baseUrl}/login";

    public string GetRecruitmentPostUrl(Guid postId)
        => $"{_baseUrl}/login";

    public string GetProfilePostUrl(Guid postId)
        => $"{_baseUrl}/login";
}
