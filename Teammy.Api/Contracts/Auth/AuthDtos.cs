namespace Teammy.Api.Contracts
{

    public sealed record LoginRequest(string IdToken);

    public sealed class AuthResponse
    {
        public string AccessToken { get; init; } = default!;
        public UserDto User { get; init; } = default!;
    }

    public sealed class UserDto
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = default!;
        public string Name { get; init; } = default!;
        public string? PhotoUrl { get; init; }
        public string Role { get; init; } = default!;
    }
}