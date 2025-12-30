using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Jobs;

public sealed class CloseTopicsOnSemesterEndJob : BackgroundService
{
    private static readonly TimeSpan MinDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromHours(12);
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloseTopicsOnSemesterEndJob> _logger;

    public CloseTopicsOnSemesterEndJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CloseTopicsOnSemesterEndJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTimeZone);
                var today = DateOnly.FromDateTime(nowVn);
                await CloseEndedSemesterTopicsAsync(today, stoppingToken);

                var nextRunUtc = await GetNextRunUtcAsync(today, stoppingToken);
                var delay = nextRunUtc - DateTime.UtcNow;
                if (delay < MinDelay)
                    delay = MinDelay;

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CloseTopicsOnSemesterEndJob failed. Retrying later.");
                await Task.Delay(FallbackDelay, stoppingToken);
            }
        }
    }

    private async Task CloseEndedSemesterTopicsAsync(DateOnly today, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await db.topics
            .Where(t => t.status == "open" && t.semester.end_date.HasValue && t.semester.end_date.Value < today)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.status, t => "closed"), ct);

        if (affected > 0)
            _logger.LogInformation("Closed {Count} topics for ended semesters.", affected);
    }

    private async Task<DateTime> GetNextRunUtcAsync(DateOnly today, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nextEndDate = await db.semesters.AsNoTracking()
            .Where(s => s.end_date.HasValue && s.end_date.Value >= today)
            .OrderBy(s => s.end_date)
            .Select(s => s.end_date)
            .FirstOrDefaultAsync(ct);

        if (nextEndDate is null)
            return DateTime.UtcNow.Add(FallbackDelay);

        var runDate = nextEndDate.Value.AddDays(1);
        var localRun = new DateTime(runDate.Year, runDate.Month, runDate.Day, 0, 5, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localRun, VietnamTimeZone);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}
