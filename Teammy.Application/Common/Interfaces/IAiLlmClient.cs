using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Ai.Models;

namespace Teammy.Application.Common.Interfaces;

public interface IAiLlmClient
{
    Task<AiLlmRerankResponse> RerankAsync(AiLlmRerankRequest request, CancellationToken ct);

    Task<AiSkillExtractionResponse> ExtractSkillsAsync(AiSkillExtractionRequest request, CancellationToken ct);
}
