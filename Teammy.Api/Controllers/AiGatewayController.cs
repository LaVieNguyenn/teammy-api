using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teammy.Infrastructure.Ai;

namespace Teammy.Api.Controllers;

[ApiController]
[Route("api/ai-gateway")]
public sealed class AiGatewayController(AiGatewayClient gateway) : ControllerBase
{
    [HttpGet("health")]
    //[Authorize(Roles = "admin,moderator")]
    public async Task<ActionResult<object>> HealthAsync(CancellationToken ct)
    {
        var result = await gateway.GetHealthAsync(ct);
        var payload = new
        {
            upstreamStatus = result.StatusCode,
            upstreamBody = result.Body
        };

        if (result.IsSuccess)
            return Ok(payload);

        return StatusCode(StatusCodes.Status502BadGateway, payload);
    }
}
