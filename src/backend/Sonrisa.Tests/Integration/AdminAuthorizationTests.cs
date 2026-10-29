using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Sonrisa.Tests.Integration;

/// <summary>
/// AC3: anonymous → 401 and normal user → 403 on every admin endpoint. The routes are read from the app's endpoint
/// data source, so a new admin endpoint is covered without editing this test.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed partial class AdminAuthorizationTests(PostgresFixture postgres) : IAsyncLifetime
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
    public void Admin_routes_are_found_in_the_endpoint_data_source()
    {
        // Guards the enumeration itself (spec §8): if controller actions were missing here, the other tests
        // would pass on an empty list.
        var routes = AdminRoutes().Select(r => r.Path).ToList();
        Assert.Contains("/api/admin/users", routes);
        Assert.Contains("/api/admin/audit", routes);
        Assert.Contains("/api/admin/events", routes);
        Assert.Contains("/api/admin/events/simulate", routes);
    }

    [Fact]
    public async Task Every_admin_endpoint_returns_401_for_anonymous_callers()
    {
        var failures = new List<string>();
        foreach (var (method, path) in AdminRoutes())
        {
            var response = await _client.SendAuthorizedAsync(method, path, accessToken: null);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{method} {path} → {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_admin_endpoint_returns_403_for_a_normal_user()
    {
        var (_, tokens) = await _client.RegisterAndLoginAsync();

        var failures = new List<string>();
        foreach (var (method, path) in AdminRoutes())
        {
            var response = await _client.SendAuthorizedAsync(method, path, tokens.AccessToken);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{method} {path} → {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_admin_endpoint_admits_the_seeded_admin()
    {
        // Proves the role claim is in the bearer token (spec §8): without it the admin would get 403 too.
        // A GET must succeed. A write sent without a body must fail validation (400, or 415 without a content
        // type), which only happens after authorization passed; 401, 403, 404 and 5xx still fail the test.
        // Route parameters get the seeded admin's own user ID, so a GET by ID finds a row (the only parameter of an
        // admin route today is a user ID).
        var tokens = await _client.LoginAsAdminAsync();
        var adminId = await AdminUserIdAsync(tokens.AccessToken);

        var failures = new List<string>();
        foreach (var (method, path) in AdminRoutes(adminId))
        {
            var response = await _client.SendAuthorizedAsync(method, path, tokens.AccessToken);
            var admitted = method == HttpMethod.Get
                ? response.IsSuccessStatusCode
                : response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType;
            if (!admitted)
            {
                failures.Add($"{method} {path} → {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Me_returns_roles_of_the_caller()
    {
        var (email, userTokens) = await _client.RegisterAndLoginAsync();
        var adminTokens = await _client.LoginAsAdminAsync();

        var user = await GetMeAsync(userTokens.AccessToken);
        Assert.Equal(email, user.Email);
        Assert.Empty(user.Roles);

        var admin = await GetMeAsync(adminTokens.AccessToken);
        Assert.Equal(ApiFactory.AdminEmail, admin.Email);
        Assert.Equal(["Admin"], admin.Roles);

        var anonymous = await _client.SendAuthorizedAsync(HttpMethod.Get, "/api/me", accessToken: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private async Task<Contracts.Dtos.Account.MeDto> GetMeAsync(string accessToken)
    {
        var response = await _client.SendAuthorizedAsync(HttpMethod.Get, "/api/me", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await System.Net.Http.Json.HttpContentJsonExtensions
            .ReadFromJsonAsync<Contracts.Dtos.Account.MeDto>(response.Content, TestApi.Ct))!;
    }

    private async Task<string> AdminUserIdAsync(string accessToken)
    {
        _ = accessToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<Application.Abstractions.IUserDirectory>()
            .FindIdByEmailAsync(ApiFactory.AdminEmail, TestApi.Ct))!;
    }

    /// <summary>Every (method, path) under <c>/api/admin</c>; route parameters get <paramref name="parameter"/>.</summary>
    private List<(HttpMethod Method, string Path)> AdminRoutes(string parameter = "1")
    {
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var routes = new List<(HttpMethod, string)>();

        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var raw = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!raw.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = RouteParameter().Replace(raw, parameter);
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            routes.AddRange(methods.Select(m => (new HttpMethod(m), path)));
        }

        return routes.Distinct().ToList();
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
