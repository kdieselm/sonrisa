using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Destinations;
using Sonrisa.Contracts.Dtos.Events;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>Calls of the user API (destinations, alerts) and database reads shared by the S3b tests.</summary>
public static class UserApi
{
    public static Task<HttpResponseMessage> SendJsonAsync(this HttpClient client, HttpMethod method, string path,
        string? token, object? body = null) =>
        client.SendAuthorizedAsync(method, path, token, body is null ? null : JsonContent.Create(body));

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    public static async Task<DestinationDto> CreateDestinationAsync(this HttpClient client, string token,
        string channelKey, object? config = null, string name = "My destination") =>
        await (await client.SendJsonAsync(HttpMethod.Post, "/api/destinations", token,
                new { channelKey, displayName = name, config }))
            .ReadAsync<DestinationDto>(HttpStatusCode.Created);

    public static async Task<AlertDto> CreateAlertAsync(this HttpClient client, string token, string categoryKey,
        object rule, IEnumerable<long> destinationIds, string name = "My alert") =>
        await (await client.SendJsonAsync(HttpMethod.Post, "/api/alerts", token,
                new { name, categoryKey, rule, destinationIds = destinationIds.ToArray() }))
            .ReadAsync<AlertDto>(HttpStatusCode.Created);

    /// <summary>A unique keyword, so news events of other tests in the shared database never match.</summary>
    public static string UniqueWord() => "kw" + Guid.NewGuid().ToString("N")[..10];

    public static async Task<AdminEventDto> SimulateNewsAsync(this HttpClient client, string adminToken,
        string title) =>
        await (await client.SendJsonAsync(HttpMethod.Post, "/api/admin/events/simulate", adminToken,
                new { categoryKey = "news", title, data = new { } }))
            .ReadAsync<AdminEventDto>(HttpStatusCode.Created);

    public static async Task<List<Delivery>> DeliveriesOfAlertAsync(this WebApplicationFactory<Program> factory, long alertId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<Delivery>().AsNoTracking().Where(d => d.AlertId == alertId).OrderBy(d => d.Id)
            .ToListAsync(Ct);
    }

    public static async Task<List<AuditEntry>> AuditForAsync(this WebApplicationFactory<Program> factory, string targetType, long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = id.ToString(CultureInfo.InvariantCulture);
        return await db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.TargetType == targetType && e.TargetId == target)
            .OrderBy(e => e.Id)
            .ToListAsync(Ct);
    }
}
