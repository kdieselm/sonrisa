using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MimeKit;
using Sonrisa.Api.HostedServices;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Configuration;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;
using Sonrisa.Contracts.Dtos.Notifications;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;
using Sonrisa.Domain.ValueObjects;
using Sonrisa.Infrastructure.Channels.Email;
using Sonrisa.Infrastructure.Channels.Slack;
using Sonrisa.Infrastructure.Channels.Slack.Mock;
using Sonrisa.Infrastructure.Configuration;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>
/// The delivery path on PostgreSQL with the real worker, the real MailKit email channel (against an in-process SMTP
/// server) and the real Slack channel (against the mock receiver in the same test host): AC1, AC10, AC17 (worker),
/// AC18, AC21 and the deleted-alert / deleted-destination guard of the worker (spec §2.6, §2.7).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeliveryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string MockBase = "http://localhost/mock/slack";
    private const string PublicBase = "http://web.test";
    private const string ThrowKey = "test-throw";
    private const string HangKey = "test-hang";

    private FakeSmtpServer _smtp = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _smtp = new FakeSmtpServer();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _smtp.DisposeAsync();
    }

    /// <summary>The API with the delivery worker running fast and every channel pointed at the test doubles.</summary>
    private void Start(bool worker)
    {
        WebApplicationFactory<Program>? self = null;
        _factory = new ApiFactory(postgres.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Channels:Slack:MockBaseUrl", MockBase);
            builder.UseSetting("Channels:Slack:MockEnabled", "true");
            builder.UseSetting("Channels:Email:Host", "127.0.0.1");
            builder.UseSetting("Channels:Email:Port", _smtp.Port.ToString());
            builder.UseSetting("Delivery:WorkerEnabled", worker ? "true" : "false");
            builder.ConfigureTestServices(services =>
            {
                var slackOptions = new SlackOptions { MockBaseUrl = MockBase, MockEnabled = true };
                services.RemoveAll<SlackOptions>();
                services.AddSingleton(slackOptions);

                // Short delays instead of 10 s / 60 s; the rules themselves are unit-tested.
                services.RemoveAll<DeliveryOptions>();
                services.AddSingleton(new DeliveryOptions
                {
                    WorkerEnabled = worker,
                    PollInterval = TimeSpan.FromMilliseconds(100),
                    SendTimeout = TimeSpan.FromSeconds(1),
                    RetryDelays = [TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(150)],
                    PublicBaseUrl = PublicBase,
                });

                // The Slack channel posts to the mock receiver of this very test host.
                var invoker = new Lazy<HttpMessageInvoker>(() => new HttpMessageInvoker(self!.Server.CreateHandler()));
                services.RemoveAll<INotificationChannel>();
                services.AddSingleton<INotificationChannel>(new EmailChannel(new EmailOptions
                {
                    Host = "127.0.0.1",
                    Port = _smtp.Port,
                }));
                services.AddSingleton<INotificationChannel>(new SlackChannel(slackOptions,
                    new HttpClient(new InvokerHandler(invoker))));
                services.AddSingleton<INotificationChannel>(new MisbehavingChannel(ThrowKey, hang: false));
                services.AddSingleton<INotificationChannel>(new MisbehavingChannel(HangKey, hang: true));
            });
        });
        self = _factory;
        _client = _factory.CreateClient();
    }

    private async Task<(string Email, string UserId, string Token)> NewUserAsync()
    {
        var (email, tokens) = await _client.RegisterAndLoginAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(Ct);
        return (email, id, tokens.AccessToken);
    }

    private async Task<List<Delivery>> WaitForAsync(long alertId, Func<List<Delivery>, bool> done, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        List<Delivery> rows;
        do
        {
            rows = await _factory.DeliveriesOfAlertAsync(alertId);
            if (done(rows))
            {
                return rows;
            }

            await Task.Delay(100, Ct);
        } while (DateTime.UtcNow < deadline);

        Assert.Fail("Timed out; deliveries: " + string.Join(", ", rows.Select(r =>
            $"#{r.Id} {r.Status} attempts={r.Attempts} error={r.LastError}")));
        return rows;
    }

    private static bool AllFinished(List<Delivery> rows, int count) =>
        rows.Count == count && rows.All(r => r.Status != DeliveryStatus.Pending);

    private async Task<List<SlackMockMessage>> MockMessagesWithAsync(string text)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<SlackMockMessage>().AsNoTracking().Where(m => m.Body.Contains(text)).ToListAsync(Ct);
    }

    // ---- AC1 ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC1_a_simulated_event_reaches_the_email_inbox_and_the_slack_mock_exactly_once()
    {
        Start(worker: true);
        var (email, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email", name: "Inbox");
        var chat = await _client.CreateDestinationAsync(token, "slack", name: "Team chat");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } },
            [inbox.Id, chat.Id], "Word watch");

        var title = $"Breaking: {word} <spotted> & more";
        await _client.SimulateNewsAsync(admin.AccessToken, title);
        var rows = await WaitForAsync(alert.Id, r => AllFinished(r, 2));

        Assert.All(rows, r =>
        {
            Assert.Equal(DeliveryStatus.Sent, r.Status);
            Assert.Equal(1, r.Attempts);
            Assert.NotNull(r.SentAt);
        });

        // Exactly one email, to the account address, with the fields of spec §2.6 (AC19) and the SIMULATED label (AC9).
        var mail = Assert.Single(_smtp.Mails, m => m.Data.Contains(word, StringComparison.Ordinal)
                                                   || MimeBody(m).Contains(word, StringComparison.Ordinal));
        Assert.Equal(email, mail.To);
        var message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(mail.Data)));
        Assert.StartsWith("[SIMULATED] ", message.Subject);
        Assert.Contains("SIMULATED", message.HtmlBody);
        Assert.Contains("&lt;spotted&gt; &amp; more", message.HtmlBody);
        Assert.Contains($"{PublicBase}/alerts/{alert.Id}", message.HtmlBody);
        Assert.Contains("Word watch", message.HtmlBody);
        Assert.Contains(word, message.HtmlBody);

        // Exactly one message in the Slack mock list, with the same fields.
        var slack = Assert.Single(await MockMessagesWithAsync(word));
        Assert.Contains("SIMULATED", slack.Body);
        Assert.Contains($"{PublicBase}/alerts/{alert.Id}", slack.Body);
        Assert.Contains("Word watch", slack.Body);
        Assert.Contains("&lt;spotted&gt; &amp; more", slack.Body);

        // The admin lists show it.
        var mockList = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/admin/slack-mock?pageSize=100",
                admin.AccessToken))
            .ReadAsync<PagedResult<SlackMockMessageDto>>(HttpStatusCode.OK);
        Assert.Contains(mockList.Items, m => m.Id == slack.Id);

        // AC21: the user's page and the admin log agree, and nothing secret leaks (AC18).
        var notificationsResponse = await _client.SendJsonAsync(HttpMethod.Get, "/api/notifications?pageSize=100",
            token);
        var notificationsJson = await notificationsResponse.Content.ReadAsStringAsync(Ct);
        var notifications = JsonSerializer.Deserialize<PagedResult<NotificationDto>>(notificationsJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(2, notifications.Items.Count);
        Assert.All(notifications.Items, n => Assert.Equal("Sent", n.Status));
        Assert.DoesNotContain("lastError", notificationsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/mock/slack/hooks/", notificationsJson);

        var log = await AdminLogAsync(admin.AccessToken);
        foreach (var n in notifications.Items)
        {
            var logRow = Assert.Single(log, l => l.Id == n.Id);
            Assert.Equal(n.Status, logRow.Status);
            Assert.Equal(email, logRow.OwnerEmail);
        }
    }

    // ---- AC10 --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC10_a_slack_destination_that_answers_500_is_tried_3_times_and_ends_failed()
    {
        Start(worker: true);
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var failing = await _client.CreateDestinationAsync(token, "slack",
            new { webhookUrl = $"{MockBase}/fail" }, "Failing hook");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } }, [failing.Id]);

        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        var rows = await WaitForAsync(alert.Id, r => AllFinished(r, 1));

        var row = Assert.Single(rows);
        Assert.Equal(DeliveryStatus.Failed, row.Status);
        Assert.Equal(3, row.Attempts);
        Assert.Equal(DeliveryFailure.SendFailed, row.Failure);
        Assert.Contains("500", row.LastError);
        Assert.DoesNotContain("/mock/slack/fail", row.LastError);
        Assert.Empty(await MockMessagesWithAsync(word));

        // The user sees a short generic reason and no error text; the admin log has the last error (spec §2.12).
        var notification = Assert.Single((await (await _client.SendJsonAsync(HttpMethod.Get, "/api/notifications",
                token)).ReadAsync<PagedResult<NotificationDto>>(HttpStatusCode.OK)).Items);
        Assert.Equal("Failed", notification.Status);
        Assert.Equal("It could not be delivered.", notification.FailureReason);
        Assert.Equal(3, notification.Attempts);
        var logRow = Assert.Single(await AdminLogAsync(admin.AccessToken), l => l.Id == notification.Id);
        Assert.Equal("Failed", logRow.Status);
        Assert.Contains("500", logRow.LastError);
    }

    [Fact]
    public async Task AC10_a_slack_destination_that_answers_404_ends_failed_after_one_attempt()
    {
        Start(worker: true);
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var rejecting = await _client.CreateDestinationAsync(token, "slack",
            new { webhookUrl = $"{MockBase}/reject" }, "Rejecting hook");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } }, [rejecting.Id]);

        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        var rows = await WaitForAsync(alert.Id, r => AllFinished(r, 1));

        var row = Assert.Single(rows);
        Assert.Equal(DeliveryStatus.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("404", row.LastError);
    }

    [Fact]
    public async Task An_email_the_smtp_server_refuses_with_550_ends_failed_after_one_attempt()
    {
        Start(worker: true);
        _smtp.RcptReply = "550 5.1.1 no such user";
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } }, [inbox.Id]);

        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        var row = Assert.Single(await WaitForAsync(alert.Id, r => AllFinished(r, 1)));

        Assert.Equal(DeliveryStatus.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("550", row.LastError);
    }

    [Fact]
    public async Task An_email_the_smtp_server_defers_with_451_is_retried_until_it_goes_through()
    {
        Start(worker: true);
        _smtp.RcptRepliesOnce.Enqueue("451 4.3.0 try again later"); // first attempt deferred, then 250
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } }, [inbox.Id]);

        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        // The 451 is answered exactly once, so the outcome is fixed: one deferred attempt, one that goes through.
        // (No check of the in-between Pending state: the retry after 150 ms can be faster than the test's poll.)
        var row = Assert.Single(await WaitForAsync(alert.Id, r => AllFinished(r, 1)));
        Assert.Equal(DeliveryStatus.Sent, row.Status);
        Assert.Equal(2, row.Attempts);
        Assert.Empty(_smtp.RcptRepliesOnce);
        Assert.Single(_smtp.Mails, m => m.Data.Contains(word, StringComparison.Ordinal)
                                        || MimeBody(m).Contains(word, StringComparison.Ordinal));
    }

    // ---- AC17 (worker part) ------------------------------------------------------------------------------------

    [Fact]
    public async Task AC17_a_channel_that_throws_and_one_that_hangs_do_not_stop_the_other_deliveries()
    {
        Start(worker: true);
        var (_, userId, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email", name: "Inbox");
        var throwing = await AddDestinationAsync(userId, ThrowKey);
        var hanging = await AddDestinationAsync(userId, HangKey);
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } },
            [throwing, hanging, inbox.Id]);

        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        var rows = await WaitForAsync(alert.Id, r => AllFinished(r, 3), seconds: 45);

        var byDestination = rows.ToDictionary(r => r.DestinationId);
        Assert.Equal(DeliveryStatus.Sent, byDestination[inbox.Id].Status);
        Assert.Equal(DeliveryStatus.Failed, byDestination[throwing].Status);
        Assert.Equal(3, byDestination[throwing].Attempts);
        Assert.Contains("InvalidOperationException", byDestination[throwing].LastError);
        Assert.DoesNotContain("secret-token", byDestination[throwing].LastError);
        Assert.Equal(DeliveryStatus.Failed, byDestination[hanging].Status);
        Assert.Equal(3, byDestination[hanging].Attempts);
        Assert.Contains("timed out", byDestination[hanging].LastError);
    }

    // ---- deleted alert / destination (S3b review finding 1) ----------------------------------------------------

    [Fact]
    public async Task A_pending_row_of_a_deleted_alert_or_destination_is_failed_and_never_sent()
    {
        Start(worker: false);
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email");
        var chat = await _client.CreateDestinationAsync(token, "slack");
        var word = UserApi.UniqueWord();
        var deletedAlert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } },
            [inbox.Id], "Doomed alert");
        var deletedDestination = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } },
            [chat.Id], "Doomed destination");
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");

        // A delete that commits after the match, as in the race of the S3b review: the rows are still Pending.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            await db.Set<Alert>().Where(a => a.Id == deletedAlert.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, now), Ct);
            await db.Set<Destination>().Where(d => d.Id == chat.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeletedAt, now), Ct);
        }

        Assert.All(await _factory.DeliveriesOfAlertAsync(deletedAlert.Id),
            d => Assert.Equal(DeliveryStatus.Pending, d.Status));
        await _factory.Services.GetRequiredService<DeliveryWorker>().RunRoundAsync(Ct);

        var alertRow = Assert.Single(await _factory.DeliveriesOfAlertAsync(deletedAlert.Id));
        Assert.Equal((DeliveryStatus.Failed, DeliveryFailure.AlertDeleted, 0),
            (alertRow.Status, alertRow.Failure, alertRow.Attempts));
        var destinationRow = Assert.Single(await _factory.DeliveriesOfAlertAsync(deletedDestination.Id));
        Assert.Equal((DeliveryStatus.Failed, DeliveryFailure.DestinationDeleted, 0),
            (destinationRow.Status, destinationRow.Failure, destinationRow.Attempts));
        Assert.DoesNotContain(_smtp.Mails, m => m.Data.Contains(word, StringComparison.Ordinal));
        Assert.Empty(await MockMessagesWithAsync(word));
    }

    // ---- review fixes (S4 review 1, 3, 4) ------------------------------------------------------------------

    [Fact]
    public async Task An_email_the_server_accepted_is_sent_even_if_it_never_answers_QUIT()
    {
        _smtp.DropAfterData = true;
        var channel = new EmailChannel(new EmailOptions { Host = "127.0.0.1", Port = _smtp.Port });
        var marker = UserApi.UniqueWord();

        var result = await channel.SendAsync("""{"address":"someone@example.test"}""",
            new FormattedMessage($"subject {marker}", "<p>hi</p>"), Ct).WaitAsync(TimeSpan.FromSeconds(15), Ct);

        Assert.Equal(SendOutcome.Sent, result.Outcome);
        Assert.Single(_smtp.Mails, m => m.Data.Contains(marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_delete_that_fails_the_row_during_a_send_wins_over_the_workers_outcome()
    {
        Start(worker: false);
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } }, [inbox.Id]);
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        var id = Assert.Single(await _factory.DeliveriesOfAlertAsync(alert.Id)).Id;

        // The worker has read the row ...
        await using var workerScope = _factory.Services.CreateAsyncScope();
        var repository = workerScope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        var work = (await repository.GetWorkAsync(id, Ct))!;

        // ... the user deletes the alert (a set-based update on the same row) ...
        await using (var deleteScope = _factory.Services.CreateAsyncScope())
        {
            await deleteScope.ServiceProvider.GetRequiredService<IDeliveryRepository>()
                .FailPendingForAlertAsync(alert.Id, DeliveryFailure.AlertDeleted, "alert deleted", Ct);
        }

        // ... and the worker's outcome must not overwrite it.
        work.Delivery.BeginAttempt(DateTimeOffset.UtcNow.AddMinutes(1));
        work.Delivery.RecordSent(DateTimeOffset.UtcNow);
        Assert.False(await repository.SaveAsync(Ct));

        var row = Assert.Single(await _factory.DeliveriesOfAlertAsync(alert.Id));
        Assert.Equal((DeliveryStatus.Failed, DeliveryFailure.AlertDeleted, 0), (row.Status, row.Failure, row.Attempts));
    }

    [Fact]
    public async Task The_slack_mock_keeps_only_the_newest_messages()
    {
        Start(worker: false);
        await using var scope = _factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISlackMockStore>();
        for (var i = 0; i < SlackMockMessage.MaxStored + 5; i++)
        {
            await store.AddAsync("cap", $"message {i}", Ct);
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Set<SlackMockMessage>().CountAsync(Ct) <= SlackMockMessage.MaxStored);
        Assert.True(await db.Set<SlackMockMessage>().AnyAsync(m => m.Body == $"message {SlackMockMessage.MaxStored + 4}", Ct));
        Assert.False(await db.Set<SlackMockMessage>().AnyAsync(m => m.Body == "message 0", Ct));
    }

    [Fact]
    public async Task The_slack_mock_refuses_a_message_that_is_too_large()
    {
        Start(worker: false);

        var response = await _client.PostAsync("/mock/slack/hooks/big", new StringContent(new string('x', 50_000)), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // ---- admin delivery log ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_admin_delivery_log_filters_by_status_and_refuses_an_unknown_status()
    {
        Start(worker: false);
        var (_, _, token) = await NewUserAsync();
        var admin = await _client.LoginAsAdminAsync();
        var inbox = await _client.CreateDestinationAsync(token, "email");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { word } }, [inbox.Id]);
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        var id = Assert.Single(await _factory.DeliveriesOfAlertAsync(alert.Id)).Id;

        var pending = await AdminLogAsync(admin.AccessToken, "Pending");
        Assert.Contains(pending, l => l.Id == id);
        Assert.All(pending, l => Assert.Equal("Pending", l.Status));
        Assert.DoesNotContain(await AdminLogAsync(admin.AccessToken, "sent"), l => l.Id == id);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.SendJsonAsync(HttpMethod.Get, "/api/admin/deliveries?status=bogus", admin.AccessToken))
            .StatusCode);
    }

    [Fact]
    public async Task The_slack_mock_receiver_is_closed_when_it_is_not_enabled()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();

        var response = await _client.PostAsync("/mock/slack/hooks/abc", new StringContent("{\"text\":\"x\"}"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/mock/slack/fail", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_slack_mock_receiver_stores_a_message_and_answers_the_documented_codes()
    {
        Start(worker: false);
        var name = "t" + Guid.NewGuid().ToString("N")[..8];
        var text = "hello-" + Guid.NewGuid().ToString("N");

        var ok = await _client.PostAsync($"/mock/slack/hooks/{name}", new StringContent($$"""{"text":"{{text}}"}"""), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await _client.PostAsync("/mock/slack/fail", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/mock/slack/reject", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsync("/mock/slack/hooks/bad%20name", new StringContent("{}"), Ct)).StatusCode);

        var stored = Assert.Single(await MockMessagesWithAsync(text));
        Assert.Equal(name, stored.HookName);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private async Task<List<AdminDeliveryDto>> AdminLogAsync(string adminToken, string? status = null) =>
        (await (await _client.SendJsonAsync(HttpMethod.Get,
                "/api/admin/deliveries?pageSize=100" + (status is null ? "" : $"&status={status}"), adminToken))
            .ReadAsync<PagedResult<AdminDeliveryDto>>(HttpStatusCode.OK)).Items.ToList();

    /// <summary>A destination of a test-only channel, written straight to the database (the API offers real channels).</summary>
    private async Task<long> AddDestinationAsync(string userId, string channelKey)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destination = new Destination(userId, channelKey, channelKey, """{"secret":"secret-token"}""",
            DateTimeOffset.UtcNow);
        db.Set<Destination>().Add(destination);
        await db.SaveChangesAsync(Ct);
        return destination.Id;
    }

    private static string MimeBody(ReceivedMail mail)
    {
        try
        {
            var message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(mail.Data)));
            return message.Subject + message.HtmlBody;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private sealed class InvokerHandler(Lazy<HttpMessageInvoker> invoker) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            invoker.Value.SendAsync(request, ct);
    }

    /// <summary>A channel that throws on every send, or never answers (AC17).</summary>
    private sealed class MisbehavingChannel(string key, bool hang) : INotificationChannel
    {
        public string Key => key;
        public string DisplayName => key;

        public DataValidationResult ValidateConfig(JsonElement config, ChannelContext context) =>
            DataValidationResult.Valid("{}");

        public string MaskConfig(string configJson) => "{}";

        public string DefaultConfig(ChannelContext context) => "{}";

        public FormattedMessage Format(NotificationContent content) => new(null, content.Title);

        public async Task<SendResult> SendAsync(string configJson, FormattedMessage message, CancellationToken ct)
        {
            if (hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            throw new InvalidOperationException("boom secret-token");
        }
    }
}
