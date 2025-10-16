using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces.Auth
{
    public record ExternalUserInfo(
        string Sub, string Email, bool EmailVerified, string DisplayName, string? PhotoUrl);

    public interface IExternalTokenVerifier
    {
        Task<ExternalUserInfo> VerifyAsync(string idToken, CancellationToken ct);
    }
}