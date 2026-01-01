using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Ai;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Ai.Indexing;

public sealed class AiIndexOutboxWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);
    private const int BatchSize = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiGatewayClient _gateway;
    private readonly ILogger<AiIndexOutboxWorker> _logger;
    private readonly IOptionsMonitor<AiIndexOutboxWorkerOptions> _options;
    private bool _loggedDisabled;

    public AiIndexOutboxWorker(
        IServiceScopeFactory scopeFactory,
        AiGatewayClient gateway,
        ILogger<AiIndexOutboxWorker> logger,
        IOptionsMonitor<AiIndexOutboxWorkerOptions> options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_options.CurrentValue.Active)
                {
                    if (!_loggedDisabled)
                    {
                        _logger.LogInformation("AiIndexOutboxWorker is disabled via config ({Section}:Active=false).", AiIndexOutboxWorkerOptions.SectionName);
                        _loggedDisabled = true;
                    }

                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                _loggedDisabled = false;

                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI index outbox worker loop failed");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sources = scope.ServiceProvider.GetRequiredService<IAiIndexSourceQueries>();

        var items = await db.Set<AiIndexOutboxItem>()
            .Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (items.Count == 0)
            return 0;

        var hadFailures = false;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (item.Action == AiIndexAction.Delete)
                {
                    await _gateway.DeleteAsync(item.PointId, ct);
                    item.ProcessedAtUtc = DateTime.UtcNow;
                    item.LastError = null;
                    continue;
                }

                var request = await BuildUpsertRequestAsync(item, sources, ct);
                if (request is null)
                {
                    item.RetryCount++;
                    item.LastError = "Source not found";
                    hadFailures = true;
                    continue;
                }

                await _gateway.UpsertAsync(request, ct);
                item.ProcessedAtUtc = DateTime.UtcNow;
                item.LastError = null;
            }
            catch (Exception ex)
            {
                item.RetryCount++;
                item.LastError = ex.Message;
                hadFailures = true;
                _logger.LogWarning(ex, "Failed to process AI index outbox item {ItemId} (type={Type}, entity={Entity})", item.Id, item.Type, item.EntityId);
            }
        }

        await db.SaveChangesAsync(ct);

        // If there were failures, avoid a tight retry loop that spams logs and hammers the DB/gateway.
        if (hadFailures)
            await Task.Delay(ErrorDelay, ct);

        return items.Count;
    }

    private static async Task<AiGatewayUpsertRequest?> BuildUpsertRequestAsync(
        AiIndexOutboxItem item,
        IAiIndexSourceQueries sources,
        CancellationToken ct)
    {
        return item.Type switch
        {
            "topic" => await BuildTopicPayloadAsync(item, sources, ct),
            "recruitment_post" => await BuildRecruitmentPayloadAsync(item, sources, ct),
            "profile_post" => await BuildProfilePayloadAsync(item, sources, ct),
            _ => null
        };
    }

    private static async Task<AiGatewayUpsertRequest?> BuildTopicPayloadAsync(
        AiIndexOutboxItem item,
        IAiIndexSourceQueries sources,
        CancellationToken ct)
    {
        var row = await sources.GetTopicAsync(item.EntityId, ct);
        if (row is null)
            return null;

        var builder = new StringBuilder();
        builder.AppendLine(row.Title);
        if (!string.IsNullOrWhiteSpace(row.Description))
            builder.AppendLine(row.Description);
        if (row.SkillNames.Count > 0)
            builder.AppendLine("Skills: " + string.Join(", ", row.SkillNames));
        if (!string.IsNullOrWhiteSpace(row.SkillsJson))
            builder.AppendLine("SkillsJson: " + row.SkillsJson);

        return new AiGatewayUpsertRequest(
            Type: "topic",
            EntityId: row.TopicId.ToString(),
            Title: row.Title,
            Text: builder.ToString(),
            SemesterId: row.SemesterId.ToString(),
            MajorId: row.MajorId?.ToString(),
            PointId: item.PointId);
    }

    private static async Task<AiGatewayUpsertRequest?> BuildRecruitmentPayloadAsync(
        AiIndexOutboxItem item,
        IAiIndexSourceQueries sources,
        CancellationToken ct)
    {
        var row = await sources.GetRecruitmentPostAsync(item.EntityId, ct);
        if (row is null)
            return null;

        var text = $"{row.Title}\n{row.Description ?? string.Empty}\nMajor: {row.MajorName}\nGroup: {row.GroupName}\nPositionNeeded: {row.PositionNeeded}\nRequiredSkills: {row.RequiredSkills}";

        return new AiGatewayUpsertRequest(
            Type: "recruitment_post",
            EntityId: row.PostId.ToString(),
            Title: row.Title,
            Text: text,
            SemesterId: row.SemesterId.ToString(),
            MajorId: row.MajorId?.ToString(),
            PointId: item.PointId);
    }

    private static async Task<AiGatewayUpsertRequest?> BuildProfilePayloadAsync(
        AiIndexOutboxItem item,
        IAiIndexSourceQueries sources,
        CancellationToken ct)
    {
        var row = await sources.GetProfilePostAsync(item.EntityId, ct);
        if (row is null)
            return null;

        var text = $"{row.Title}\n{row.Description ?? string.Empty}\nOwner: {row.OwnerDisplayName}\nPrimaryRole: {row.PrimaryRole}\nSkillsText: {row.SkillsText}\nSkillsJson: {row.SkillsJson}";

        return new AiGatewayUpsertRequest(
            Type: "profile_post",
            EntityId: row.PostId.ToString(),
            Title: row.Title,
            Text: text,
            SemesterId: row.SemesterId.ToString(),
            MajorId: row.MajorId?.ToString(),
            PointId: item.PointId);
    }
}
