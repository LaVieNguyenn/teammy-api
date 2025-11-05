namespace Teammy.Application.Common.Interfaces;

public interface IAppUrlProvider
{
    // Returns a full URL that the invitee can click to view the invitation in the frontend
    string GetInvitationUrl(Guid invitationId, Guid groupId);
}

