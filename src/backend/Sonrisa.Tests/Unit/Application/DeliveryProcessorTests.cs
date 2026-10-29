using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Configuration;
using Sonrisa.Application.Registries;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Domain.Entities;
using Sonrisa.Domain.ValueObjects;

namespace Sonrisa.Tests.Unit.Application;

/// <summary>
/// Spec §2.7, architecture §3.2, AC10, AC17: attempt count, delays, final failure, deleted alerts and destinations,
/// timeouts. The channel is a fake that returns what the test says.
/// </summary>
public sealed class DeliveryProcessorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly TestClock _clock = new(T0);
    private readonly FakeChannel _channel = new();
    private readonly DeliveryOptions _options = new();

    private (DeliveryProcessor Processor, FakeRepository Repo) Build(Action<Alert>? alert = null,
        Action<Destination>? destination = null)
    {
        var rule = new NewsCategory().ValidateRule(JsonSerializer.SerializeToElement(new { keywords = new[] { "flood" } }))
            .DataJson!;
        var theAlert = new Alert("user-1", "Floods", "news", rule, [1], T0);
        var theDestination = new Destination("user-1", FakeChannel.ChannelKey, "Hook", """{"secret":"s3cr3t-token"}""", T0);
        var item = new Event("news", "guardian-world", "n1", "Flood in the north", null, "https://example.test/n", T0, T0,
            false, NewsCategory.Serialize(new NewsData("Guardian")));
        alert?.Invoke(theAlert);
        destination?.Invoke(theDestination);
        var repo = new FakeRepository(new DeliveryWork(new Delivery(1, 1, 1, T0), theAlert, theDestination, item));
        var categories = new CategoryRegistry([new NewsCategory()]);
        var processor = new DeliveryProcessor(repo, categories, new ChannelRegistry([_channel]),
            new SourceRegistry([], categories), _options, _clock);
        return (processor, repo);
    }

    [Fact]
    public async Task A_successful_send_marks_the_delivery_sent_once()
    {
        var (processor, repo) = Build();
        _channel.Results.Enqueue(SendResult.Sent());

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(ProcessOutcome.Sent, result.Outcome);
        Assert.Equal(DeliveryStatus.Sent, repo.Delivery.Status);
        Assert.Equal(1, repo.Delivery.Attempts);
        Assert.Equal(T0, repo.Delivery.SentAt);
        Assert.Null(repo.Delivery.NextAttemptAt);
        Assert.Single(_channel.Sent);

        // A second call finds nothing to do and sends nothing again.
        Assert.Equal(ProcessOutcome.Skipped, (await processor.ProcessAsync(1, Ct)).Outcome);
        Assert.Single(_channel.Sent);
    }

    [Fact]
    public async Task The_message_has_the_display_name_of_the_source_and_the_absolute_alert_link()
    {
        var (processor, _) = Build();
        _channel.Results.Enqueue(SendResult.Sent());

        await processor.ProcessAsync(1, Ct);

        var content = _channel.Contents.Single();
        Assert.Equal("guardian-world", content.SourceName);
        Assert.Equal("http://localhost:8080/alerts/0", content.AlertUrl);
        Assert.Contains("flood", content.KeyValue);
    }

    [Fact]
    public async Task A_retryable_error_is_tried_again_after_10_s_then_60_s_and_fails_on_the_third_attempt()
    {
        var (processor, repo) = Build();
        _channel.Results.Enqueue(SendResult.Retryable("first"));
        _channel.Results.Enqueue(SendResult.Retryable("second"));
        _channel.Results.Enqueue(SendResult.Retryable("third"));

        var first = await processor.ProcessAsync(1, Ct);
        Assert.Equal(ProcessOutcome.WillRetry, first.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(10), first.RetryIn);
        Assert.Equal(T0.AddSeconds(10), repo.Delivery.NextAttemptAt);
        Assert.Equal(DeliveryStatus.Pending, repo.Delivery.Status);

        // Not due yet: nothing happens.
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(ProcessOutcome.Skipped, (await processor.ProcessAsync(1, Ct)).Outcome);
        Assert.Equal(1, repo.Delivery.Attempts);

        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = await processor.ProcessAsync(1, Ct);
        Assert.Equal(ProcessOutcome.WillRetry, second.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(60), second.RetryIn);

        _clock.Advance(TimeSpan.FromSeconds(60));
        var third = await processor.ProcessAsync(1, Ct);
        Assert.Equal(ProcessOutcome.Failed, third.Outcome);
        Assert.Equal(DeliveryStatus.Failed, repo.Delivery.Status);
        Assert.Equal(3, repo.Delivery.Attempts);
        Assert.Equal("third", repo.Delivery.LastError);
        Assert.Equal(DeliveryFailure.SendFailed, repo.Delivery.Failure);
        Assert.Null(repo.Delivery.NextAttemptAt);
        Assert.Equal(3, _channel.Sent.Count);

        // Failed is final: no fourth attempt, however long we wait.
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(ProcessOutcome.Skipped, (await processor.ProcessAsync(1, Ct)).Outcome);
        Assert.Equal(3, _channel.Sent.Count);
    }

    [Fact]
    public async Task A_permanent_error_fails_after_one_attempt()
    {
        var (processor, repo) = Build();
        _channel.Results.Enqueue(SendResult.Permanent("Slack answered 404."));

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(ProcessOutcome.Failed, result.Outcome);
        Assert.Equal(1, repo.Delivery.Attempts);
        Assert.Equal("Slack answered 404.", repo.Delivery.LastError);
        Assert.Equal(DeliveryFailure.SendFailed, repo.Delivery.Failure);
    }

    [Fact]
    public async Task Retry_after_replaces_the_delay_and_still_counts_as_an_attempt()
    {
        var (processor, repo) = Build();
        _channel.Results.Enqueue(SendResult.Retryable("429", TimeSpan.FromSeconds(120)));

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryIn);
        Assert.Equal(T0.AddSeconds(120), repo.Delivery.NextAttemptAt);
        Assert.Equal(1, repo.Delivery.Attempts);
    }

    [Fact]
    public async Task Retry_after_is_capped_at_5_minutes()
    {
        var (processor, repo) = Build();
        _channel.Results.Enqueue(SendResult.Retryable("429", TimeSpan.FromHours(2)));

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(TimeSpan.FromMinutes(5), result.RetryIn);
        Assert.Equal(T0.AddMinutes(5), repo.Delivery.NextAttemptAt);
    }

    [Fact]
    public async Task A_deleted_alert_is_failed_and_nothing_is_sent()
    {
        var (processor, repo) = Build(alert: a => a.SoftDelete(T0));

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(ProcessOutcome.Failed, result.Outcome);
        Assert.Equal(DeliveryFailure.AlertDeleted, repo.Delivery.Failure);
        Assert.Equal(0, repo.Delivery.Attempts);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task A_deleted_destination_is_failed_and_nothing_is_sent()
    {
        var (processor, repo) = Build(destination: d => d.SoftDelete(T0));

        await processor.ProcessAsync(1, Ct);

        Assert.Equal(DeliveryFailure.DestinationDeleted, repo.Delivery.Failure);
        Assert.Equal(DeliveryStatus.Failed, repo.Delivery.Status);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task A_send_that_hangs_is_cut_off_by_the_timeout_and_retried()
    {
        _options.SendTimeout = TimeSpan.FromMilliseconds(100);
        var (processor, repo) = Build();
        _channel.Hang = true;

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(ProcessOutcome.WillRetry, result.Outcome);
        Assert.Contains("timed out", repo.Delivery.LastError);
        Assert.Equal(DeliveryStatus.Pending, repo.Delivery.Status);
    }

    [Fact]
    public async Task A_channel_that_throws_is_retried_and_its_message_is_not_stored()
    {
        var (processor, repo) = Build();
        _channel.Throw = new InvalidOperationException("boom https://hooks.slack.com/services/SECRET");

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(ProcessOutcome.WillRetry, result.Outcome);
        Assert.Contains("InvalidOperationException", repo.Delivery.LastError);
        Assert.DoesNotContain("SECRET", repo.Delivery.LastError);
    }

    [Fact]
    public async Task A_host_shutdown_during_a_send_leaves_the_delivery_pending()
    {
        var (processor, repo) = Build();
        _channel.Hang = true;
        using var stopping = new CancellationTokenSource();
        var task = processor.ProcessAsync(1, stopping.Token);
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(DeliveryStatus.Pending, repo.Delivery.Status);
        Assert.Equal(0, repo.Delivery.Attempts);
    }

    [Fact]
    public async Task A_category_that_cannot_read_the_stored_event_fails_the_delivery_without_retries()
    {
        var (processor, repo) = Build(alert: a => a.ChangeRule("{\"keywords\":5}", T0.AddHours(1)));

        var result = await processor.ProcessAsync(1, Ct);

        Assert.Equal(ProcessOutcome.Failed, result.Outcome);
        Assert.Equal(DeliveryFailure.SendFailed, repo.Delivery.Failure);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public void The_last_delay_repeats_and_a_missing_list_means_no_delay()
    {
        var options = new DeliveryOptions();

        Assert.Equal(TimeSpan.FromSeconds(60), DeliveryProcessor.NextDelay(options, 5, null));

        options.RetryDelays.Clear();
        Assert.Equal(TimeSpan.Zero, DeliveryProcessor.NextDelay(options, 1, null));
    }

    private sealed class FakeChannel : INotificationChannel
    {
        public const string ChannelKey = "fake";

        public Queue<SendResult> Results { get; } = new();
        public List<FormattedMessage> Sent { get; } = [];
        public List<NotificationContent> Contents { get; } = [];
        public bool Hang { get; set; }
        public Exception? Throw { get; set; }

        public string Key => ChannelKey;
        public string DisplayName => "Fake";

        public DataValidationResult ValidateConfig(JsonElement config, ChannelContext context) =>
            DataValidationResult.Valid("{}");

        public string MaskConfig(string configJson) => "{}";

        public string DefaultConfig(ChannelContext context) => "{}";

        public FormattedMessage Format(NotificationContent content)
        {
            Contents.Add(content);
            return new FormattedMessage("subject", content.Title);
        }

        public async Task<SendResult> SendAsync(string configJson, FormattedMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return Results.Count > 0 ? Results.Dequeue() : SendResult.Sent();
        }
    }

    private sealed class FakeRepository(DeliveryWork work) : IDeliveryRepository
    {
        public Delivery Delivery => work.Delivery;

        public Task<DeliveryWork?> GetWorkAsync(long id, CancellationToken ct) => Task.FromResult<DeliveryWork?>(work);

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;

        public void Add(Delivery delivery) => throw new NotSupportedException();

        public Task<int> FailPendingForAlertAsync(long alertId, DeliveryFailure failure, string error,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<int> FailPendingForDestinationAsync(long destinationId, DeliveryFailure failure, string error,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<long>> ListDueIdsAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<AdminDeliveryRow> Items, int Total)> ListForAdminAsync(DeliveryStatus? status,
            int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<(IReadOnlyList<NotificationRow> Items, int Total)> ListForUserAsync(string userId, int page,
            int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<NotificationRow?> GetForUserAsync(long id, string userId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
