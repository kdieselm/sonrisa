using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Configuration;
using Sonrisa.Application.Registries;
using Sonrisa.Domain.Entities;
using Sonrisa.Domain.ValueObjects;

namespace Sonrisa.Application.Services;

public enum ProcessOutcome
{
    /// <summary>Missing, not pending any more, or not due yet: nothing was done.</summary>
    Skipped,
    Sent,

    /// <summary>The attempt failed and another one is scheduled.</summary>
    WillRetry,
    Failed,
}

/// <summary>What happened to one delivery; the worker logs it. <see cref="Error"/> holds no secret.</summary>
public sealed record ProcessResult(ProcessOutcome Outcome, int Attempt = 0, string? Error = null,
    TimeSpan? RetryIn = null, string? ChannelKey = null);

/// <summary>
/// Sends one pending delivery (spec §2.7, architecture §3.2): checks that the alert and the destination still exist,
/// asks the category for the content, lets the channel format and send it with a timeout, and records the outcome.
/// The channel classifies the result; this class owns the attempt count and the delays. One row per call, so one
/// failing or hanging destination cannot block the others (T36, AC17).
/// </summary>
public sealed class DeliveryProcessor(
    IDeliveryRepository deliveries,
    CategoryRegistry categories,
    ChannelRegistry channels,
    SourceRegistry sources,
    DeliveryOptions settings,
    TimeProvider time)
{
    public async Task<ProcessResult> ProcessAsync(long deliveryId, CancellationToken stopping)
    {
        var work = await deliveries.GetWorkAsync(deliveryId, stopping);
        var now = time.GetUtcNow();
        if (work is null || work.Delivery.Status != DeliveryStatus.Pending || work.Delivery.NextAttemptAt > now)
        {
            return new ProcessResult(ProcessOutcome.Skipped);
        }

        var (delivery, alert, destination, item) = work;

        // A delete that committed after the match leaves the row pending: check before every send (S3b review 1).
        if (alert.IsDeleted)
        {
            return await FailWithoutAttemptAsync(delivery, DeliveryFailure.AlertDeleted, "alert deleted", stopping);
        }

        if (destination.IsDeleted)
        {
            return await FailWithoutAttemptAsync(delivery, DeliveryFailure.DestinationDeleted, "destination deleted",
                stopping);
        }

        var category = categories.Find(alert.CategoryKey);
        var channel = channels.Find(destination.ChannelKey);
        if (category is null || channel is null)
        {
            return await FailWithoutAttemptAsync(delivery, DeliveryFailure.SendFailed,
                category is null ? "unknown category" : "unknown channel", stopping);
        }

        FormattedMessage message;
        try
        {
            var content = category.Describe(item, alert) with
            {
                SourceName = sources.DisplayNameOf(item.SourceKey),
                AlertUrl = AlertUrl(alert.Id),
            };
            message = channel.Format(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Stored data this category cannot read: waiting will not help.
            return await FailAsync(delivery, $"The message could not be built ({ex.GetType().Name}).", stopping);
        }

        var result = await SendAsync(channel, destination.Config, message, stopping);
        var attempt = delivery.Attempts + 1;
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                delivery.RecordSent(now);
                await deliveries.SaveAsync(stopping);
                return new ProcessResult(ProcessOutcome.Sent, attempt, null, null, channel.Key);

            case SendOutcome.Retryable when attempt < settings.MaxAttempts:
                var delay = NextDelay(settings, attempt, result.RetryAfter);
                delivery.RecordRetry(result.Error ?? "Send failed.", now + delay);
                await deliveries.SaveAsync(stopping);
                return new ProcessResult(ProcessOutcome.WillRetry, attempt, result.Error, delay, channel.Key);

            default:
                delivery.RecordFinalFailure(result.Error ?? "Send failed.");
                await deliveries.SaveAsync(stopping);
                return new ProcessResult(ProcessOutcome.Failed, attempt, result.Error, null, channel.Key);
        }
    }

    /// <summary>
    /// The channel's attempt under the send timeout. A timeout and an unexpected exception are retryable; a stop of
    /// the host is not an outcome, it propagates and the row stays pending.
    /// </summary>
    private async Task<SendResult> SendAsync(INotificationChannel channel, string config, FormattedMessage message,
        CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(settings.SendTimeout);
        try
        {
            return await channel.SendAsync(config, message, timeout.Token);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            return SendResult.Retryable($"The send timed out after {settings.SendTimeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type only: the message of an unexpected exception might carry a URL.
            return SendResult.Retryable($"Unexpected error ({ex.GetType().Name}).");
        }
    }

    /// <summary>Delay after attempt <paramref name="attempt"/>: the list entry, or the destination's Retry-After (capped).</summary>
    public static TimeSpan NextDelay(DeliveryOptions settings, int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wanted)
        {
            return wanted > settings.MaxRetryAfter ? settings.MaxRetryAfter : wanted;
        }

        var delays = settings.RetryDelays;
        return delays.Count == 0 ? TimeSpan.Zero : delays[Math.Min(attempt - 1, delays.Count - 1)];
    }

    private async Task<ProcessResult> FailWithoutAttemptAsync(Delivery delivery, DeliveryFailure failure,
        string error, CancellationToken ct)
    {
        delivery.Fail(failure, error);
        await deliveries.SaveAsync(ct);
        return new ProcessResult(ProcessOutcome.Failed, delivery.Attempts, error);
    }

    private async Task<ProcessResult> FailAsync(Delivery delivery, string error, CancellationToken ct)
    {
        delivery.RecordFinalFailure(error);
        await deliveries.SaveAsync(ct);
        return new ProcessResult(ProcessOutcome.Failed, delivery.Attempts, error);
    }

    private string AlertUrl(long alertId) =>
        $"{settings.PublicBaseUrl.TrimEnd('/')}/alerts/{alertId}";
}
