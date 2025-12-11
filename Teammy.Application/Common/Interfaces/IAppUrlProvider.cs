namespace Teammy.Application.Common.Interfaces;

public interface IAppUrlProvider
{
    string GetInvitationUrl(Guid invitationId, Guid groupId);
    string GetRecruitmentPostUrl(Guid postId);
    string GetProfilePostUrl(Guid postId);
}
