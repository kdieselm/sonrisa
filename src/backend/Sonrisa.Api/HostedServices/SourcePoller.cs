using Microsoft.Extensions.Options;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Application.Services;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Api.HostedServices;

/// <summary>
/// Polls every registered source in its own loop on its own interval (spec §2.5, architecture §3.1). The first poll
/// runs at startup. A failing fetch or item is logged and the loop goes on, so one source cannot stop the others
/// (T26, AC17). Each poll gets its own DI scope; each item its own transaction (<see cref="EventIngestor"/>).
/// </summary>
public sealed class SourcePoller(
    SourceRegistry sources,
    IOptions<SourcesOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<SourcePoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PollingEnabled)
        {
            logger.LogInformation("Source polling is disabled (Sources:PollingEnabled = false).");
            return;
        }

        await Task.Yield();
        logger.LogInformation("Polling {Count} sources: {Sources}.", sources.All.Count,
            string.Join(", ", sources.All.Select(s => $"{s.Key} every {s.Interval}")));
        await Task.WhenAll(sources.All.Select(source => RunAsync(source, stoppingToken)));
    }

    private async Task RunAsync(IEventSource source, CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(source.Interval, time);
            do
            {
                try
                {
                    await PollOnceAsync(source, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Last guard: whatever escapes one poll must not end this source's loop silently.
                    logger.LogError(ex, "Source {Source}: poll failed; next try in {Interval}.", source.Key,
                        source.Interval);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private async Task PollOnceAsync(IEventSource source, CancellationToken stoppingToken)
    {
        IReadOnlyList<IncomingEvent> items;
        try
        {
            items = await source.FetchAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Source {Source}: fetch failed; next try in {Interval}.", source.Key, source.Interval);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var ingestor = scope.ServiceProvider.GetRequiredService<EventIngestor>();
        int inserted = 0, updated = 0, failed = 0, queued = 0;
        foreach (var item in items)
        {
            try
            {
                var result = await ingestor.IngestAsync(source.Key, source.CategoryKey, item, isSimulated: false,
                    stoppingToken);
                inserted += result.Outcome == IngestOutcome.Inserted ? 1 : 0;
                updated += result.Outcome == IngestOutcome.Updated ? 1 : 0;
                queued += result.DeliveriesCreated;
                if (result.FailedRuleAlertIds is { Count: > 0 } broken)
                {
                    logger.LogWarning("Source {Source}: item {ExternalId}: the rule of alerts {AlertIds} could not be "
                                      + "evaluated; they were skipped.", source.Key, item.ExternalId, broken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Source {Source}: item {ExternalId} could not be stored.", source.Key,
                    item.ExternalId);
            }
        }

        logger.LogInformation(
            "Source {Source}: {Count} items, {Inserted} new, {Updated} updated, {Failed} failed, {Queued} deliveries "
            + "queued.", source.Key, items.Count, inserted, updated, failed, queued);
    }
}
