using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Events;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>AC9 (simulated label), spec §2.9–§2.10: admin event list, filters and the audited simulate form.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminEventsTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Admin_simulates_a_disaster_event_that_is_labelled_listed_and_audited()
    {
        var admin = await _client.LoginAsAdminAsync();
        var title = $"Simulated quake {Guid.NewGuid():N}";

        var response = await SimulateAsync(admin.AccessToken, new
        {
            categoryKey = "disaster",
            title,
            url = "https://example.test/quake",
            data = new { magnitude = 6.1, place = "Test Valley", latitude = 10.5, longitude = -20.25 },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<AdminEventDto>(Ct))!;
        Assert.True(created.IsSimulated);
        Assert.Equal(("disaster", "simulated", title), (created.CategoryKey, created.SourceKey, created.Title));
        Assert.Equal(created.FirstSeenAt, created.PublishedAt);
        Assert.Equal(6.1, created.Data.GetProperty("magnitude").GetDouble());
        Assert.Equal(-20.25, created.Data.GetProperty("longitude").GetDouble());

        var page = await ListAsync(admin.AccessToken, "?category=disaster&source=simulated&pageSize=100");
        var listed = Assert.Single(page.Items, e => e.Id == created.Id);
        Assert.True(listed.IsSimulated);
        Assert.Equal("Simulated (admin)", listed.SourceName);
        Assert.All(page.Items, e => Assert.Equal(("disaster", "simulated"), (e.CategoryKey, e.SourceKey)));

        var audit = Assert.Single(await AuditForEventAsync(created.Id));
        Assert.Equal((AuditActions.SimulateEvent, AuditOutcome.Success), (audit.Action, audit.Outcome));
        Assert.NotNull(audit.ActorUserId);
    }

    [Fact]
    public async Task Simulating_a_news_event_defaults_the_feed_name()
    {
        var admin = await _client.LoginAsAdminAsync();

        var response = await SimulateAsync(admin.AccessToken,
            new { categoryKey = "news", title = "Earthquake hits test city", data = new { } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<AdminEventDto>(Ct))!;
        Assert.Equal("Simulated feed", created.Data.GetProperty("feedName").GetString());
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_field_errors_and_creates_nothing()
    {
        var admin = await _client.LoginAsAdminAsync();
        var before = await CountAsync();

        var unknown = await SimulateAsync(admin.AccessToken, new { categoryKey = "weather", title = "x", data = new { } });
        Assert.Contains("categoryKey", (await ProblemAsync(unknown)).Errors.Keys);

        var invalid = await SimulateAsync(admin.AccessToken, new
        {
            categoryKey = "disaster",
            title = "",
            url = "javascript:alert(1)",
            data = new { magnitude = 12 },
        });
        var errors = (await ProblemAsync(invalid)).Errors.Keys;
        Assert.Contains("title", errors);
        Assert.Contains("url", errors);
        Assert.Contains("data.magnitude", errors);
        Assert.Contains("data.place", errors);

        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task Filters_list_the_registered_categories_and_sources()
    {
        var admin = await _client.LoginAsAdminAsync();

        var response = await _client.SendAuthorizedAsync(HttpMethod.Get, "/api/admin/events/filters", admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var filters = (await response.Content.ReadFromJsonAsync<EventFiltersDto>(Ct))!;

        Assert.Equal(["disaster", "news"], filters.Categories.Select(c => c.Key).Order());
        Assert.Equal(["simulated", "usgs", "world-news"], filters.Sources.Select(s => s.Key).Order());
    }

    [Fact]
    public async Task List_is_newest_first_and_paged()
    {
        var admin = await _client.LoginAsAdminAsync();
        for (var i = 0; i < 3; i++)
        {
            var created = await SimulateAsync(admin.AccessToken, new { categoryKey = "news", title = $"N{i}", data = new { } });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var first = await ListAsync(admin.AccessToken, "?page=1&pageSize=2");
        var second = await ListAsync(admin.AccessToken, "?page=2&pageSize=2");
        Assert.Equal(2, first.Items.Count);
        Assert.Empty(first.Items.Select(e => e.Id).Intersect(second.Items.Select(e => e.Id)));
        var all = first.Items.Concat(second.Items).ToList();
        Assert.Equal(all.OrderByDescending(e => e.FirstSeenAt).ThenByDescending(e => e.Id).Select(e => e.Id),
            all.Select(e => e.Id));

        var capped = await ListAsync(admin.AccessToken, "?pageSize=5000");
        Assert.Equal(100, capped.PageSize);
    }

    [Fact]
    public async Task Normal_user_cannot_simulate()
    {
        var (_, tokens) = await _client.RegisterAndLoginAsync();
        var before = await CountAsync();

        var response = await SimulateAsync(tokens.AccessToken, new { categoryKey = "news", title = "x", data = new { } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await CountAsync());
    }

    private Task<HttpResponseMessage> SimulateAsync(string accessToken, object body) =>
        _client.SendAuthorizedAsync(HttpMethod.Post, "/api/admin/events/simulate", accessToken,
            JsonContent.Create(body));

    private async Task<PagedResult<AdminEventDto>> ListAsync(string accessToken, string query)
    {
        var response = await _client.SendAuthorizedAsync(HttpMethod.Get, "/api/admin/events" + query, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<AdminEventDto>>(Ct))!;
    }

    private static async Task<ValidationProblemDetails> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!;
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Event>().CountAsync(Ct);
    }

    private async Task<List<AuditEntry>> AuditForEventAsync(long id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.Set<AuditEntry>()
            .Where(e => e.TargetType == AuditTargets.Event && e.TargetId == target)
            .ToListAsync(Ct);
    }
}
