using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Notifications;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>
/// Matching at ingestion and its delivery rows on PostgreSQL: AC5 (disabled alerts), AC7 (timing), AC13 (unique
/// index), AC14 (recent matches), AC16 (re-ingestion), AC21 (Notifications page), and the delete paths of spec §2.6.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MatchingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>A user with an email and a Slack destination and one news alert on a unique keyword.</summary>
    private async Task<(string Token, AlertDto Alert, long Email, long Slack, string Word)> UserWithNewsAlertAsync()
    {
        var (_, user) = await _client.RegisterAndLoginAsync();
        var email = await _client.CreateDestinationAsync(user.AccessToken, "email", name: "Inbox");
        var slack = await _client.CreateDestinationAsync(user.AccessToken, "slack", name: "Team chat");
        var word = UserApi.UniqueWord();
        var alert = await _client.CreateAlertAsync(user.AccessToken, "news", new { keywords = new[] { word } },
            [email.Id, slack.Id], "Word watch");
        return (user.AccessToken, alert, email.Id, slack.Id, word);
    }

    private async Task<List<NotificationDto>> NotificationsAsync(string token) =>
        (await (await _client.SendJsonAsync(HttpMethod.Get, "/api/notifications?pageSize=100", token))
            .ReadAsync<PagedResult<NotificationDto>>(HttpStatusCode.OK)).Items.ToList();

    [Fact]
    public async Task A_matching_simulated_event_appears_on_the_notifications_page_once_per_destination()
    {
        var (token, alert, email, slack, word) = await UserWithNewsAlertAsync();
        var admin = await _client.LoginAsAdminAsync();

        var matching = await _client.SimulateNewsAsync(admin.AccessToken, $"Breaking: {word} spotted");
        await _client.SimulateNewsAsync(admin.AccessToken, $"Nothing about {word}x here");

        var rows = await NotificationsAsync(token);
        Assert.Equal(2, rows.Count);
        Assert.Equal([email, slack], (await _factory.DeliveriesOfAlertAsync(alert.Id)).Select(d => d.DestinationId).Order());
        Assert.All(rows, r =>
        {
            Assert.Equal(("Pending", "Word watch", matching.Title, true), (r.Status, r.AlertName, r.EventTitle, r.EventIsSimulated));
            Assert.Null(r.FailureReason);
        });
        Assert.Equal(["Email", "Slack"], rows.Select(r => r.ChannelName).Order());
        Assert.Equal(["Inbox", "Team chat"], rows.Select(r => r.DestinationName).Order());
    }

    [Fact]
    public async Task Notifications_list_only_the_callers_own_rows_and_another_users_id_is_404()
    {
        var (ownerToken, _, _, _, word) = await UserWithNewsAlertAsync();
        var (otherToken, _, _, _, _) = await UserWithNewsAlertAsync();
        var admin = await _client.LoginAsAdminAsync();
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} again");

        var ownerRows = await NotificationsAsync(ownerToken);
        Assert.Equal(2, ownerRows.Count);
        Assert.Empty(await NotificationsAsync(otherToken));

        foreach (var row in ownerRows)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await _client.SendJsonAsync(HttpMethod.Get, $"/api/notifications/{row.Id}", ownerToken)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await _client.SendJsonAsync(HttpMethod.Get, $"/api/notifications/{row.Id}", otherToken)).StatusCode);
        }
    }

    [Fact]
    public async Task A_disabled_or_deleted_alert_does_not_match_and_re_enabling_matches_again()
    {
        var (token, alert, _, _, word) = await UserWithNewsAlertAsync();
        var admin = await _client.LoginAsAdminAsync();

        await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/disable", token);
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} while disabled");
        Assert.Empty(await _factory.DeliveriesOfAlertAsync(alert.Id));

        await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/enable", token);
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} after enabling");
        Assert.Equal(2, (await _factory.DeliveriesOfAlertAsync(alert.Id)).Count);

        await _client.SendJsonAsync(HttpMethod.Delete, $"/api/alerts/{alert.Id}", token);
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} after deleting");
        Assert.Equal(2, (await _factory.DeliveriesOfAlertAsync(alert.Id)).Count);
    }

    [Fact]
    public async Task Only_alerts_active_before_the_event_was_first_seen_are_candidates()
    {
        var (_, alert, _, _, _) = await UserWithNewsAlertAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

        var atActiveSince = await alerts.ListMatchCandidatesAsync("news", alert.ActiveSince, Ct);
        var justAfter = await alerts.ListMatchCandidatesAsync("news", alert.ActiveSince.AddTicks(10), Ct);

        Assert.DoesNotContain(atActiveSince, c => c.AlertId == alert.Id);
        Assert.Contains(justAfter, c => c.AlertId == alert.Id);
    }

    [Fact]
    public async Task An_event_published_more_than_two_hours_ago_or_re_ingested_creates_no_delivery()
    {
        var (_, alert, _, _, word) = await UserWithNewsAlertAsync();
        var source = "test-" + Guid.NewGuid().ToString("N")[..8];
        var data = NewsCategory.Serialize(new NewsData("Test feed"));

        var old = await IngestAsync(source, new IncomingEvent("old", $"{word} from yesterday", null, null,
            DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(-1), data));
        Assert.Equal((IngestOutcome.Inserted, 0), (old.Outcome, old.DeliveriesCreated));

        var fresh = new IncomingEvent("fresh", $"{word} right now", null, null, DateTimeOffset.UtcNow.AddMinutes(-5), data);
        Assert.Equal(2, (await IngestAsync(source, fresh)).DeliveriesCreated);
        Assert.Equal(IngestOutcome.Unchanged, (await IngestAsync(source, fresh)).Outcome);
        var revised = await IngestAsync(source, fresh with { Title = $"{word} right now (updated)" });
        Assert.Equal((IngestOutcome.Updated, 0), (revised.Outcome, revised.DeliveriesCreated));

        Assert.Equal(2, (await _factory.DeliveriesOfAlertAsync(alert.Id)).Count); // AC16: still only the first match
    }

    [Fact]
    public async Task The_unique_index_rejects_a_second_delivery_for_the_same_alert_event_and_destination()
    {
        var (token, alert, email, _, word) = await UserWithNewsAlertAsync();
        var admin = await _client.LoginAsAdminAsync();
        var ev = await _client.SimulateNewsAsync(admin.AccessToken, $"{word} once");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<Delivery>().Add(new Delivery(alert.Id, ev.Id, email, DateTimeOffset.UtcNow));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("IX_Deliveries_AlertId_EventId_DestinationId", ex.InnerException!.Message);
        Assert.Equal(2, (await NotificationsAsync(token)).Count);
    }

    [Fact]
    public async Task Event_and_deliveries_commit_together_and_roll_back_together()
    {
        var (_, alert, _, _, word) = await UserWithNewsAlertAsync();
        var source = "test-" + Guid.NewGuid().ToString("N")[..8];

        // PostgreSQL rejects the event (invalid jsonb) when the transaction saves; the deliveries matched for it in
        // the same transaction must not survive either.
        await Assert.ThrowsAnyAsync<Exception>(() => IngestAsync(source,
            new IncomingEvent("bad", $"{word} broken", null, null, DateTimeOffset.UtcNow, "{\"feedName\":")));

        Assert.Empty(await _factory.DeliveriesOfAlertAsync(alert.Id));
    }

    [Fact]
    public async Task Rule_and_config_are_stored_as_jsonb()
    {
        var (_, alert, email, _, word) = await UserWithNewsAlertAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var types = await db.Database.SqlQuery<string>(
                $"""
                 SELECT pg_typeof("Rule")::text AS "Value" FROM "Alerts" WHERE "Id" = {alert.Id}
                 UNION ALL SELECT pg_typeof("Config")::text FROM "Destinations" WHERE "Id" = {email}
                 """)
            .ToListAsync(Ct);
        var keyword = await db.Database.SqlQuery<string>(
                $"""SELECT "Rule"->'keywords'->>0 AS "Value" FROM "Alerts" WHERE "Id" = {alert.Id}""")
            .SingleAsync(Ct);

        Assert.Equal(["jsonb", "jsonb"], types);
        Assert.Equal(word, keyword);
    }

    [Fact]
    public async Task Deleting_an_alert_or_a_destination_fails_its_pending_deliveries_and_keeps_the_history()
    {
        var (token, alert, email, slack, word) = await UserWithNewsAlertAsync();
        var admin = await _client.LoginAsAdminAsync();
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} first");

        // Destination deleted: its row fails, it is unlinked; the other destination stays pending.
        await _client.SendJsonAsync(HttpMethod.Delete, $"/api/destinations/{slack}", token);
        var rows = await _factory.DeliveriesOfAlertAsync(alert.Id);
        Assert.Equal((DeliveryStatus.Failed, DeliveryFailure.DestinationDeleted),
            rows.Where(r => r.DestinationId == slack).Select(r => (r.Status, r.Failure)).Single());
        Assert.Equal(DeliveryStatus.Pending, rows.Single(r => r.DestinationId == email).Status);
        var linked = await (await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{alert.Id}", token))
            .ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.Equal([email], linked.DestinationIds);

        // Alert deleted: the remaining pending row fails; both rows stay on the page with a generic reason only.
        await _client.SendJsonAsync(HttpMethod.Delete, $"/api/alerts/{alert.Id}", token);
        var page = await NotificationsAsync(token);
        Assert.Equal(2, page.Count);
        Assert.All(page, r => Assert.Equal("Failed", r.Status));
        Assert.Contains(page, r => r.DestinationDeleted && r.FailureReason!.Contains("destination was deleted"));
        Assert.Contains(page, r => r.AlertDeleted && r.FailureReason!.Contains("alert was deleted"));

        var body = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/notifications", token)).Content
            .ReadAsStringAsync(Ct);
        Assert.DoesNotContain("destination deleted", body); // LastError text never reaches the user
        Assert.DoesNotContain("lastError", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recent_matches_list_at_most_three_events_of_the_last_24_hours_and_send_nothing()
    {
        var (token, alert, _, _, word) = await UserWithNewsAlertAsync();
        var source = "test-" + Guid.NewGuid().ToString("N")[..8];
        var data = NewsCategory.Serialize(new NewsData("Test feed"));

        // Already ingested before the alert existed: older than 24 h, four recent matches, one recent non-match.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.Set<Event>().Add(new Event("news", source, "e0", $"{word} two days ago", null, null, now.AddHours(-25),
                now.AddHours(-25), false, data));
            for (var i = 1; i <= 4; i++)
            {
                db.Set<Event>().Add(new Event("news", source, $"e{i}", $"{word} story {i}", null, null,
                    now.AddHours(-10 + i), now.AddHours(-10 + i), false, data));
            }

            db.Set<Event>().Add(new Event("news", source, "e5", "Unrelated story", null, null, now.AddHours(-1),
                now.AddHours(-1), false, data));
            await db.SaveChangesAsync(Ct);
        }

        var recent = await (await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{alert.Id}/recent-matches", token))
            .ReadAsync<RecentMatchesDto>(HttpStatusCode.OK);

        Assert.True(recent.Supported);
        Assert.Equal([$"{word} story 4", $"{word} story 3", $"{word} story 2"], recent.Items.Select(i => i.Title));
        Assert.Empty(await _factory.DeliveriesOfAlertAsync(alert.Id));
    }

    [Fact]
    public async Task Recent_matches_are_empty_with_a_reason_for_a_category_without_back_fill()
    {
        await using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IEventCategory, NoBackfillCategory>()));
        using var client = factory.CreateClient();
        var (_, user) = await client.RegisterAndLoginAsync();
        var destination = await client.CreateDestinationAsync(user.AccessToken, "email");
        var alert = await client.CreateAlertAsync(user.AccessToken, NoBackfillCategory.CategoryKey, new { },
            [destination.Id]);

        var recent = await (await client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{alert.Id}/recent-matches",
            user.AccessToken)).ReadAsync<RecentMatchesDto>(HttpStatusCode.OK);

        Assert.False(recent.Supported);
        Assert.Equal(NoBackfillCategory.Reason, recent.Reason);
        Assert.Empty(recent.Items);
    }

    private async Task<IngestResult> IngestAsync(string source, IncomingEvent item)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EventIngestor>()
            .IngestAsync(source, "news", item, false, Ct);
    }

    /// <summary>Stands in for market (S5): a category whose past events say nothing about an alert.</summary>
    private sealed class NoBackfillCategory : IEventCategory
    {
        public const string CategoryKey = "test-no-backfill";
        public const string Reason = "No history for this category.";

        public string Key => CategoryKey;
        public string DisplayName => "Test";
        public TimeSpan? MaxAge => null;
        public bool SupportsBackfill => false;
        public string? BackfillUnavailableReason => Reason;

        public DataValidationResult ValidateSimulatedData(JsonElement data) => DataValidationResult.Valid("{}");
        public DataValidationResult ValidateRule(JsonElement rule) => DataValidationResult.Valid("{}");
        public bool Matches(Event item, string ruleJson) => true;

        public NotificationContent Describe(Event item, Alert alert) =>
            new(item.Title, DisplayName, "Key", "value", item.SourceKey, item.Url, item.IsSimulated, "test",
                $"/alerts/{alert.Id}", null);
    }
}
