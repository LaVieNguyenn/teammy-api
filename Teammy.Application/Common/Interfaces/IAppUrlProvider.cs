namespace Teammy.Application.Common.Interfaces;

public interface IAppUrlProvider
{
    string GetInvitationUrl(Guid invitationId, Guid groupId);
}

