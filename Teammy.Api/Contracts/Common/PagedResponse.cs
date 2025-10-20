namespace Teammy.Api.Contracts.Common
{
    public sealed record PagedResponse<T>(int Total, int Page, int Size, IReadOnlyList<T> Items);
}
