namespace Teammy.Api.Contracts.Common
{
    public sealed record ApiResponse(bool Ok, string? Message = null, object? Data = null, int StatusCode = 200);
}
