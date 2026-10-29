using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Application.Abstractions;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Catalog;
using Sonrisa.Contracts.Dtos.Destinations;
using Sonrisa.Domain.Entities;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>
/// AC4 (isolation), AC5 (alert actions), AC12 (alert and destination audit), AC18 (no full webhook URL in any
/// response) on the real API and PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AlertsApiTests(PostgresFixture postgres) : IAsyncLifetime
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

    [Fact]
    public async Task Alert_create_list_edit_disable_enable_and_delete_work_through_the_api()
    {
        var (_, user) = await _client.RegisterAndLoginAsync();
        var token = user.AccessToken;
        var email = await _client.CreateDestinationAsync(token, "email");
        var slack = await _client.CreateDestinationAsync(token, "slack");

        var created = await _client.CreateAlertAsync(token, "disaster", new { minMagnitude = 6 }, [email.Id],
            "Big quakes");
        Assert.True(created.Enabled);
        Assert.Equal(6, created.Rule.GetProperty("minMagnitude").GetDouble());

        var list = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/alerts", token))
            .ReadAsync<PagedResult<AlertDto>>(HttpStatusCode.OK);
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);

        var edited = await (await _client.SendJsonAsync(HttpMethod.Put, $"/api/alerts/{created.Id}", token,
                new { name = "Quakes near Tokyo", rule = new { minMagnitude = 5, near = new { latitude = 35.7, longitude = 139.7, radiusKm = 300 } }, destinationIds = new[] { email.Id, slack.Id } }))
            .ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.Equal("Quakes near Tokyo", edited.Name);
        Assert.Equal(300, edited.Rule.GetProperty("near").GetProperty("radiusKm").GetDouble());
        Assert.Equal([email.Id, slack.Id], edited.DestinationIds);
        Assert.True(edited.ActiveSince > created.ActiveSince); // a rule change resets it

        var disabled = await (await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{created.Id}/disable", token))
            .ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.False(disabled.Enabled);
        Assert.Equal(edited.ActiveSince, disabled.ActiveSince);

        var enabled = await (await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{created.Id}/enable", token))
            .ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.True(enabled.Enabled);
        Assert.True(enabled.ActiveSince > disabled.ActiveSince); // AC5: re-enabling resets it

        var delete = await _client.SendJsonAsync(HttpMethod.Delete, $"/api/alerts/{created.Id}", token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{created.Id}", token)).StatusCode);
        var after = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/alerts", token))
            .ReadAsync<PagedResult<AlertDto>>(HttpStatusCode.OK);
        Assert.Empty(after.Items);
    }

    [Fact]
    public async Task Invalid_alerts_get_field_errors()
    {
        var (_, user) = await _client.RegisterAndLoginAsync();
        var destination = await _client.CreateDestinationAsync(user.AccessToken, "email");

        var response = await _client.SendJsonAsync(HttpMethod.Post, "/api/alerts", user.AccessToken,
            new { name = "", categoryKey = "news", rule = new { keywords = Array.Empty<string>() }, destinationIds = Array.Empty<long>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"name\"", body);
        Assert.Contains("\"rule.keywords\"", body);
        Assert.Contains("\"destinationIds\"", body);

        var unknown = await _client.SendJsonAsync(HttpMethod.Post, "/api/alerts", user.AccessToken,
            new { name = "x", categoryKey = "weather", rule = new { }, destinationIds = new[] { destination.Id } });
        Assert.Contains("\"categoryKey\"", await unknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Another_users_alert_or_destination_is_404_and_nothing_changes()
    {
        var (_, owner) = await _client.RegisterAndLoginAsync();
        var (_, other) = await _client.RegisterAndLoginAsync();
        var ownerDestination = await _client.CreateDestinationAsync(owner.AccessToken, "email", name: "Owner's");
        var ownerAlert = await _client.CreateAlertAsync(owner.AccessToken, "news",
            new { keywords = new[] { "flood" } }, [ownerDestination.Id], "Owner's alert");
        var otherDestination = await _client.CreateDestinationAsync(other.AccessToken, "email");
        var otherAlert = await _client.CreateAlertAsync(other.AccessToken, "news",
            new { keywords = new[] { "fire" } }, [otherDestination.Id]);

        var t = other.AccessToken;
        var attempts = new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, $"/api/alerts/{ownerAlert.Id}", null),
            (HttpMethod.Put, $"/api/alerts/{ownerAlert.Id}", new { name = "Mine now", rule = new { keywords = new[] { "x" } }, destinationIds = new[] { otherDestination.Id } }),
            (HttpMethod.Post, $"/api/alerts/{ownerAlert.Id}/disable", null),
            (HttpMethod.Post, $"/api/alerts/{ownerAlert.Id}/enable", null),
            (HttpMethod.Get, $"/api/alerts/{ownerAlert.Id}/recent-matches", null),
            (HttpMethod.Delete, $"/api/alerts/{ownerAlert.Id}", null),
            (HttpMethod.Get, $"/api/destinations/{ownerDestination.Id}", null),
            (HttpMethod.Put, $"/api/destinations/{ownerDestination.Id}", new { displayName = "Mine now" }),
            (HttpMethod.Delete, $"/api/destinations/{ownerDestination.Id}", null),
            // Linking: a new alert and an existing one of the caller may not point at someone else's destination.
            (HttpMethod.Post, "/api/alerts", new { name = "Sneaky", categoryKey = "news", rule = new { keywords = new[] { "x" } }, destinationIds = new[] { ownerDestination.Id } }),
            (HttpMethod.Put, $"/api/alerts/{otherAlert.Id}", new { name = "Sneaky", rule = new { keywords = new[] { "fire" } }, destinationIds = new[] { otherDestination.Id, ownerDestination.Id } }),
        };

        foreach (var (method, path, body) in attempts)
        {
            var response = await _client.SendJsonAsync(method, path, t, body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path} → {(int)response.StatusCode}");
        }

        // The owner's data is unchanged; the other user's alert still links only their own destination.
        var alert = await (await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{ownerAlert.Id}", owner.AccessToken))
            .ReadAsync<AlertDto>(HttpStatusCode.OK);
        Assert.Equal(("Owner's alert", true, ownerAlert.ActiveSince), (alert.Name, alert.Enabled, alert.ActiveSince));
        var destination = await (await _client.SendJsonAsync(HttpMethod.Get, $"/api/destinations/{ownerDestination.Id}", owner.AccessToken))
            .ReadAsync<DestinationDto>(HttpStatusCode.OK);
        Assert.Equal("Owner's", destination.DisplayName);
        var others = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/alerts", t))
            .ReadAsync<PagedResult<AlertDto>>(HttpStatusCode.OK);
        Assert.Equal([otherDestination.Id], Assert.Single(others.Items).DestinationIds);
        var otherDestinations = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/destinations", t))
            .ReadAsync<PagedResult<DestinationDto>>(HttpStatusCode.OK);
        Assert.Equal([otherDestination.Id], otherDestinations.Items.Select(d => d.Id));
    }

    [Fact]
    public async Task Every_alert_and_destination_change_writes_exactly_one_audit_entry_without_the_webhook_url()
    {
        var (_, user) = await _client.RegisterAndLoginAsync();
        var token = user.AccessToken;
        var secret = "S" + Guid.NewGuid().ToString("N");
        var url = $"https://hooks.slack.com/services/T000/B000/{secret}";

        var slack = await _client.CreateDestinationAsync(token, "slack", new { webhookUrl = url });
        await _client.SendJsonAsync(HttpMethod.Put, $"/api/destinations/{slack.Id}", token,
            new { displayName = "Team", config = new { webhookUrl = url + "x" } });
        var alert = await _client.CreateAlertAsync(token, "news", new { keywords = new[] { "flood" } }, [slack.Id]);
        await _client.SendJsonAsync(HttpMethod.Put, $"/api/alerts/{alert.Id}", token,
            new { name = "Renamed", rule = new { keywords = new[] { "flood" } }, destinationIds = new[] { slack.Id } });
        await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/disable", token);
        await _client.SendJsonAsync(HttpMethod.Post, $"/api/alerts/{alert.Id}/enable", token);
        await _client.SendJsonAsync(HttpMethod.Delete, $"/api/alerts/{alert.Id}", token);
        await _client.SendJsonAsync(HttpMethod.Delete, $"/api/destinations/{slack.Id}", token);

        var alertEntries = await _factory.AuditForAsync(AuditTargets.Alert, alert.Id);
        Assert.Equal([AuditActions.AlertCreate, AuditActions.AlertUpdate, AuditActions.AlertDisable,
            AuditActions.AlertEnable, AuditActions.AlertDelete], alertEntries.Select(e => e.Action));
        var destinationEntries = await _factory.AuditForAsync(AuditTargets.Destination, slack.Id);
        Assert.Equal([AuditActions.DestinationCreate, AuditActions.DestinationUpdate, AuditActions.DestinationDelete],
            destinationEntries.Select(e => e.Action));
        Assert.All(alertEntries.Concat(destinationEntries), e =>
        {
            Assert.Equal(AuditOutcome.Success, e.Outcome);
            Assert.NotNull(e.ActorUserId);
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(e));
        });

        // A rejected change writes nothing.
        var before = (await _factory.AuditForAsync(AuditTargets.Alert, alert.Id)).Count;
        await _client.SendJsonAsync(HttpMethod.Delete, $"/api/alerts/{alert.Id}", token); // already deleted → 404
        Assert.Equal(before, (await _factory.AuditForAsync(AuditTargets.Alert, alert.Id)).Count);
    }

    [Fact]
    public async Task No_response_contains_a_full_webhook_url()
    {
        var (email, user) = await _client.RegisterAndLoginAsync();
        var token = user.AccessToken;
        var secret = "S" + Guid.NewGuid().ToString("N");
        var url = $"https://hooks.slack.com/services/T000/B000/{secret}";
        var admin = await _client.LoginAsAdminAsync();

        var bodies = new List<string>();
        async Task<HttpResponseMessage> Keep(HttpResponseMessage response)
        {
            bodies.Add(await response.Content.ReadAsStringAsync(Ct));
            return response;
        }

        var created = await (await Keep(await _client.SendJsonAsync(HttpMethod.Post, "/api/destinations", token,
            new { channelKey = "slack", displayName = "Team", config = new { webhookUrl = url } })))
            .ReadAsync<DestinationDto>(HttpStatusCode.Created);
        Assert.Equal("…" + secret[^4..], created.Config.GetProperty("webhookUrl").GetString());

        var word = UserApi.UniqueWord();
        var alert = await (await Keep(await _client.SendJsonAsync(HttpMethod.Post, "/api/alerts", token,
                new { name = "Watch", categoryKey = "news", rule = new { keywords = new[] { word } }, destinationIds = new[] { created.Id } })))
            .ReadAsync<AlertDto>(HttpStatusCode.Created);
        await Keep(await _client.SendJsonAsync(HttpMethod.Put, $"/api/alerts/{alert.Id}", token,
            new { name = "Watch 2", rule = new { keywords = new[] { word } }, destinationIds = new[] { created.Id } }));
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, "/api/alerts", token));
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{alert.Id}/recent-matches", token));
        await Keep(await _client.SendJsonAsync(HttpMethod.Post, "/api/destinations", token,
            new { channelKey = "slack", displayName = "Bad", config = new { webhookUrl = url.Replace("https://", "http://") } }));
        await _client.SimulateNewsAsync(admin.AccessToken, $"{word} happened");
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, $"/api/destinations/{created.Id}", token));
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, "/api/destinations", token));
        await Keep(await _client.SendJsonAsync(HttpMethod.Put, $"/api/destinations/{created.Id}", token,
            new { displayName = "Renamed" })); // config omitted: kept, still masked in the answer
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, "/api/catalog", token));
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, "/api/notifications", token));
        await Keep(await _client.SendJsonAsync(HttpMethod.Get, $"/api/alerts/{alert.Id}", token));

        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            userId = (await scope.ServiceProvider.GetRequiredService<IUserDirectory>().FindIdByEmailAsync(email, Ct))!;
        }

        var detail = await (await Keep(await _client.SendJsonAsync(HttpMethod.Get, $"/api/admin/users/{userId}",
            admin.AccessToken))).ReadAsync<AdminUserDetailDto>(HttpStatusCode.OK);
        Assert.Equal(created.Id, Assert.Single(detail.Destinations).Id);
        Assert.Equal(alert.Id, Assert.Single(detail.Alerts).Id);

        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain("hooks.slack.com/services", body);
        });

        // The update without a config kept the stored URL (matching still targets it): only the name changed.
        var kept = await (await _client.SendJsonAsync(HttpMethod.Get, $"/api/destinations/{created.Id}", token))
            .ReadAsync<DestinationDto>(HttpStatusCode.OK);
        Assert.Equal(("Renamed", "…" + secret[^4..]), (kept.DisplayName, kept.Config.GetProperty("webhookUrl").GetString()));
    }

    [Fact]
    public async Task The_catalog_lists_the_registered_categories_and_channels_with_defaults()
    {
        var (email, user) = await _client.RegisterAndLoginAsync();

        var catalog = await (await _client.SendJsonAsync(HttpMethod.Get, "/api/catalog", user.AccessToken))
            .ReadAsync<CatalogDto>(HttpStatusCode.OK);

        Assert.Equal(["disaster", "market", "news"], catalog.Categories.Select(c => c.Key).Order());
        Assert.Equal(["email", "slack"], catalog.Channels.Select(c => c.Key).Order());
        Assert.Equal(email, catalog.Channels.Single(c => c.Key == "email").DefaultConfig.GetProperty("address").GetString());

        var defaulted = await _client.CreateDestinationAsync(user.AccessToken, "slack");
        Assert.StartsWith("…", defaulted.Config.GetProperty("webhookUrl").GetString());
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_user_endpoint()
    {
        // Every /api route outside the admin area, the open auth endpoints and the anonymous Slack mock (S4) is
        // enumerated from the app's endpoint list, so a new user endpoint without [Authorize] fails here.
        var dataSource = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var routes = dataSource.Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => (Path: "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'),
                Methods: e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
                         ?? ["GET"]))
            .Where(r => r.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                        && !r.Path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)
                        && !r.Path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Methods.Select(m => (Method: new HttpMethod(m),
                Path: System.Text.RegularExpressions.Regex.Replace(r.Path, @"\{[^}]+\}", "1"))))
            .Distinct()
            .ToList();

        Assert.Contains(routes, r => r.Path == "/api/alerts/1/recent-matches");
        var failures = new List<string>();
        foreach (var (method, path) in routes)
        {
            var response = await _client.SendJsonAsync(method, path, null);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{method} {path} → {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }
}
