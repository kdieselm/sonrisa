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
    /// <summary>Added to the send timeout when a row is parked during a send.</summary>
    private static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(30);

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

        // A previous run counted its attempts and then died before it could record the outcome (see BeginAttempt):
        // never send more than MaxAttempts times.
        if (delivery.Attempts >= settings.MaxAttempts)
        {
            delivery.Fail(DeliveryFailure.SendFailed, "attempts used up without a recorded outcome");
            return await SavedAsync(ProcessOutcome.Failed, delivery.Attempts, "attempts used up without a recorded outcome");
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

        // Write ahead: count the attempt and park the row for the length of the send plus a margin, saved before the
        // send. A crash or a failing save afterwards cannot make the worker send the same row again every round.
        var attempt = delivery.BeginAttempt(now + settings.SendTimeout + LeaseMargin);
        if (!await deliveries.SaveAsync(stopping))
        {
            return new ProcessResult(ProcessOutcome.Skipped);
        }

        var result = await SendAsync(channel, destination.Config, message, stopping);
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                delivery.RecordSent(now);
                return await SavedAsync(ProcessOutcome.Sent, attempt, null, null, channel.Key);

            case SendOutcome.Retryable when attempt < settings.MaxAttempts:
                var delay = NextDelay(settings, attempt, result.RetryAfter);
                delivery.RecordRetry(result.Error ?? "Send failed.", now + delay);
                return await SavedAsync(ProcessOutcome.WillRetry, attempt, result.Error, delay, channel.Key);

            default:
                delivery.RecordFinalFailure(result.Error ?? "Send failed.");
                return await SavedAsync(ProcessOutcome.Failed, attempt, result.Error, null, channel.Key);
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

    /// <summary>Delay after attempt <paramref name="attempt"/>: the list entry, or the destination's Retry-After if longer (capped).</summary>
    public static TimeSpan NextDelay(DeliveryOptions settings, int attempt, TimeSpan? retryAfter)
    {
        var delays = settings.RetryDelays;
        var scheduled = delays.Count == 0 ? TimeSpan.Zero : delays[Math.Min(attempt - 1, delays.Count - 1)];
        if (retryAfter is { } wanted)
        {
            // The destination can ask for longer than the schedule, never for shorter: "Retry-After: 0" must not burn
            // all attempts at once (S4 review 9).
            return wanted > settings.MaxRetryAfter ? settings.MaxRetryAfter : wanted < scheduled ? scheduled : wanted;
        }

        return scheduled;
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
        // Nothing was sent, so no attempt is counted (S4 review 8), same as the other failures before a send.
        return await FailWithoutAttemptAsync(delivery, DeliveryFailure.SendFailed, error, ct);
    }

    /// <summary>
    /// Saves the outcome. If the row was changed meanwhile (a delete failed it during the send) the other writer's state
    /// stands and this run reports <see cref="ProcessOutcome.Skipped"/> (S4 review 3).
    /// </summary>
    private async Task<ProcessResult> SavedAsync(ProcessOutcome outcome, int attempt, string? error,
        TimeSpan? retryIn = null, string? channelKey = null) =>
        await deliveries.SaveAsync(CancellationToken.None)
            ? new ProcessResult(outcome, attempt, error, retryIn, channelKey)
            : new ProcessResult(ProcessOutcome.Skipped, attempt, "superseded by a concurrent change", null, channelKey);

    private string AlertUrl(long alertId) =>
        $"{settings.PublicBaseUrl.TrimEnd('/')}/alerts/{alertId}";
}
