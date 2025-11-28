namespace Teammy.Application.Ai.Dtos;

public sealed record AiResponse<T>(bool Success, T? Data, string? Error)
{
    public static AiResponse<T> FromSuccess(T data) => new(true, data, null);

    public static AiResponse<T> FromError(string message) => new(false, default, message);
}
