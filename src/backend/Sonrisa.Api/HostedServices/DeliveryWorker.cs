using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Configuration;
using Sonrisa.Application.Services;

namespace Sonrisa.Api.HostedServices;

/// <summary>
/// Sends pending deliveries (spec §2.7, architecture §3.2): every few seconds it takes the due rows and hands each one
/// to <see cref="DeliveryProcessor"/> in its own DI scope and its own try/catch, so one failing row cannot stop the
/// others or the worker (AC17, T36). Exactly one instance is assumed (ADR-1), so rows are not locked.
/// </summary>
public sealed class DeliveryWorker(
    DeliveryOptions options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled)
        {
            logger.LogInformation("Delivery worker is disabled (Delivery:WorkerEnabled = false).");
            return;
        }

        await Task.Yield();
        logger.LogInformation("Delivery worker started: every {Interval}, {Attempts} attempts per delivery.",
            options.PollInterval, options.MaxAttempts);
        try
        {
            using var timer = new PeriodicTimer(options.PollInterval, time);
            do
            {
                try
                {
                    await RunRoundAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Last guard (database down, …): the next round tries again.
                    logger.LogError(ex, "Delivery round failed; next try in {Interval}.", options.PollInterval);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    /// <summary>One round: the due rows, oldest first. Public so the tests can run a round without the timer.</summary>
    public async Task RunRoundAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<long> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IDeliveryRepository>()
                .ListDueIdsAsync(time.GetUtcNow(), options.BatchSize, stoppingToken);
        }

        foreach (var id in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>()
                    .ProcessAsync(id, stoppingToken);
                Log(id, result);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Delivery {DeliveryId}: processing failed; it stays pending.", id);
            }
        }
    }

    private void Log(long id, ProcessResult result)
    {
        switch (result.Outcome)
        {
            case ProcessOutcome.Sent:
                logger.LogInformation("Delivery {DeliveryId}: sent via {Channel} (attempt {Attempt}).", id,
                    result.ChannelKey, result.Attempt);
                break;
            case ProcessOutcome.WillRetry:
                logger.LogWarning("Delivery {DeliveryId}: attempt {Attempt} via {Channel} failed ({Error}); retry in "
                                  + "{Delay}.", id, result.Attempt, result.ChannelKey, result.Error, result.RetryIn);
                break;
            case ProcessOutcome.Failed:
                logger.LogWarning("Delivery {DeliveryId}: failed after {Attempt} attempt(s) ({Error}).", id,
                    result.Attempt, result.Error);
                break;
        }
    }
}
