namespace Teammy.Application.Auth.Dtos;

public sealed record LoginResponse(
    string AccessToken,
    Guid   UserId,
    string Email,
    string DisplayName,
    string Role
);
