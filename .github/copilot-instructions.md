# Copilot Instructions (TEAMMY API)

## Big picture
- Clean Architecture: `Teammy.Domain` (entities) → `Teammy.Application` (use-cases/services/DTOs) → `Teammy.Infrastructure` (EF Core + external clients) → `Teammy.Api` (controllers + DI).
- HTTP surface is mostly controllers under `Teammy.Api/Controllers/*Controller.cs`. Group-scoped project management lives under:
  - Board/Kanban: `api/groups/{groupId}/board` in `Teammy.Api/Controllers/BoardController.cs`.
  - Backlog/Milestones/Reports: `api/groups/{groupId}/tracking` in `Teammy.Api/Controllers/ProjectTrackingController.cs`.
- AI integration is server-to-server via AiGateway:
  - HttpClients configured in `Teammy.Infrastructure/DependencyInjection.cs` using env/appsettings keys `AI_GATEWAY_BASE_URL` (required) and `AI_GATEWAY_API_KEY` (optional bearer).
  - Rerank calls are made via `Teammy.Infrastructure/Ai/AiLlmClient.cs` and consumed by `Teammy.Application/Ai/Services/AiMatchingService.cs`.

## Build & run
- Build: `dotnet build`
- Run API: `dotnet run --project Teammy.Api`
- Startup prints AI gateway wiring diagnostics in `Teammy.Api/Program.cs`.

## Conventions & patterns
- Controllers extract user id from JWT claims (`ClaimTypes.NameIdentifier` / `sub`) and pass into services (see `BoardController` and `ProjectTrackingController`). Match that pattern when adding endpoints.
- Authorization is attribute-based (`[Authorize]`); avoid adding public debug endpoints unless explicitly requested.
- Use DTOs in `Teammy.Application.*.Dtos` for request/response shapes; keep persistence models in `Teammy.Infrastructure/Persistence`.
- External calls use typed `HttpClient` registered in Infrastructure; don’t instantiate `HttpClient` manually.

## AI debugging
- Use API-side debug controller `Teammy.Api/Controllers/AiDebugController.cs` to run suggestions and return captured AiGateway request/response traces.
- Traces are captured in `Teammy.Infrastructure/Ai/AiLlmClient.cs` and stored in `IAiGatewayTraceStore`.

## Safe changes
- Prefer small, surgical edits and keep public routes stable.
- If you change AI request formats, update the corresponding shaping/extraction in `AiMatchingService` and validate via debug traces.
