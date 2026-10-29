using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Events;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>
/// Market alerts on PostgreSQL (spec §2.4): the start state written with the alert, the state written with each event,
/// AC8 end to end through the simulate form, and the user-facing asset list. Each test uses its own factory, so the
/// in-memory observation store starts empty.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MarketTests(PostgresFixture postgres) : IAsyncLifetime
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

    private void Observe(string asset, double change) =>
        _factory.Services.GetRequiredService<IObservationStore>().Update(asset, change);

    private async Task<(string Token, long Destination)> UserWithEmailAsync()
    {
        var (_, user) = await _client.RegisterAndLoginAsync();
        var email = await _client.CreateDestinationAsync(user.AccessToken, "email", name: "Inbox");
        return (user.AccessToken, email.Id);
    }

    private Task<AlertDto> MarketAlertAsync(string token, long destination, object rule, string name = "BTC") =>
        _client.CreateAlertAsync(token, "market", rule, [destination], name);

    private async Task<MarketState> StateOfAsync(long alertId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Set<AlertState>().AsNoTracking().SingleAsync(s => s.AlertId == alertId, Ct);
        return MarketCategory.DeserializeState(row.State);
    }

    private async Task SimulateAsync(string adminToken, string asset, double previous, double current) =>
        await (await _client.SendJsonAsync(HttpMethod.Post, "/api/admin/events/simulate", adminToken, new
        {
            categoryKey = "market",
            title = $"{asset} {current}",
            data = new { asset, previousChangePercent = previous, currentChangePercent = current },
        })).ReadAsync<AdminEventDto>(HttpStatusCode.Created);

    [Fact]
    public async Task A_new_market_alert_gets_its_start_state_from_the_last_observation()
    {
        var (token, destination) = await UserWithEmailAsync();
        Observe("bitcoin", 2.0);
        Observe("ethereum", 6.0);

        var armed = await MarketAlertAsync(token, destination,
            new { asset = "bitcoin", direction = "up", thresholdPercent = 5 });
        var disarmed = await MarketAlertAsync(token, destination,
            new { asset = "ethereum", direction = "up", thresholdPercent = 5, stepPercent = 2.5 }, "ETH");

        Assert.Equal(new MarketState(new MarketSide(true, null), null), await StateOfAsync(armed.Id));
        Assert.Equal(new MarketState(new MarketSide(false, 5), null), await StateOfAsync(disarmed.Id));
    }

    [Fact]
    public async Task AC8_the_documented_sequence_queues_exactly_three_deliveries()
    {
        var (token, destination) = await UserWithEmailAsync();
        var admin = await _client.LoginAsAdminAsync();
        Observe("bitcoin", 2.0);
        var alert = await MarketAlertAsync(token, destination,
            new { asset = "bitcoin", direction = "up", thresholdPercent = 5, stepPercent = 2.5 });

        var previous = 2.0;
        foreach (var value in new[] { 5.2, 5.8, 7.6, 4.5, 3.9, 5.1 })
        {
            await SimulateAsync(admin.AccessToken, "bitcoin", previous, value);
            previous = value;
        }

        var deliveries = await _factory.DeliveriesOfAlertAsync(alert.Id);
        Assert.Equal(3, deliveries.Count);
        Assert.Equal(["+5.2%", "+7.6%", "+5.1%"], await EventChangesAsync(deliveries));
    }

    private async Task<List<string>> EventChangesAsync(List<Delivery> deliveries)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = deliveries.Select(d => d.EventId).ToList();
        var events = await db.Set<Event>().AsNoTracking().Where(e => ids.Contains(e.Id)).OrderBy(e => e.Id)
            .ToListAsync(Ct);
        return events.Select(e => MarketCategory.Signed(MarketCategory.Deserialize(e.Data).CurrentChangePercent))
            .ToList();
    }

    [Fact]
    public async Task A_rule_change_and_a_re_enable_start_the_state_over()
    {
        var (token, destination) = await UserWithEmailAsync();
        var admin = await _client.LoginAsAdminAsync();
        Observe("bitcoin", 2.0);
        var alert = await MarketAlertAsync(token, destination,
            new { asset = "bitcoin", direction = "up", thresholdPercent = 5 });
        await SimulateAsync(admin.AccessToken, "bitcoin", 2.0, 5.5);
        Assert.False((await StateOfAsync(alert.Id)).Up!.Armed);

        // Rule change while the latest observation is 2.0: armed again, and only a new crossing notifies.
        await (await _client.SendJsonAsync(HttpMethod.Put, $"/api/alerts/{alert.Id}", token, new
        {
            name = "BTC",
            rule = new { asset = "bitcoin", direction = "up", thresholdPercent = 4 },
            destinationIds = new[] { destination },
        })).ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.True((await StateOfAsync(alert.Id)).Up!.Armed);

        Observe("bitcoin", 6.0);
        await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/disable", token);
        await (await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/enable", token))
            .ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.False((await StateOfAsync(alert.Id)).Up!.Armed); // 6.0 is already past 4
    }

    [Fact]
    public async Task A_disabled_market_alert_gets_no_delivery_and_no_state_change()
    {
        var (token, destination) = await UserWithEmailAsync();
        var admin = await _client.LoginAsAdminAsync();
        Observe("bitcoin", 2.0);
        var alert = await MarketAlertAsync(token, destination,
            new { asset = "bitcoin", direction = "up", thresholdPercent = 5 });
        await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/disable", token);

        await SimulateAsync(admin.AccessToken, "bitcoin", 2.0, 5.5);

        Assert.Empty(await _factory.DeliveriesOfAlertAsync(alert.Id));
        Assert.True((await StateOfAsync(alert.Id)).Up!.Armed);
    }

    [Fact]
    public async Task A_bad_market_rule_is_a_400_with_the_field_name()
    {
        var (token, destination) = await UserWithEmailAsync();

        var response = await _client.SendJsonAsync(HttpMethod.Post, "/api/alerts", token, new
        {
            name = "Bad",
            categoryKey = "market",
            rule = new { asset = "doge", direction = "up", thresholdPercent = 0.5 },
            destinationIds = new[] { destination },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("rule.asset", body);
        Assert.Contains("rule.thresholdPercent", body);
    }

    [Fact]
    public async Task The_asset_list_needs_a_login_and_comes_from_configuration()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.SendJsonAsync(HttpMethod.Get, "/api/market/assets", null)).StatusCode);

        var (token, _) = await UserWithEmailAsync();
        var assets = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/market/assets", token))
            .ReadAsync<List<MarketAsset>>(HttpStatusCode.OK);

        Assert.Equal(["bitcoin", "ethereum", "solana", "AAPL", "MSFT", "NVDA", "TSLA", "AMZN"],
            assets.Select(a => a.Key));
    }

    [Fact]
    public async Task A_market_alert_has_no_recent_matches_and_says_why()
    {
        var (token, destination) = await UserWithEmailAsync();
        var alert = await MarketAlertAsync(token, destination,
            new { asset = "bitcoin", direction = "up", thresholdPercent = 5 });

        var recent = await (await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{alert.Id}/recent-matches",
            token)).ReadAsync<RecentMatchesDto>(HttpStatusCode.OK);

        Assert.False(recent.Supported);
        Assert.Empty(recent.Items);
        Assert.False(string.IsNullOrWhiteSpace(recent.Reason));
    }
}
