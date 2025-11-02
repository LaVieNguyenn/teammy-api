namespace Teammy.Api.Contracts.Topic
{
    public sealed class ImportResponse
    {
        public string JobId { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Success { get; set; }
        public int Error { get; set; }
    }
}
