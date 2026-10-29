using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sonrisa.Tests.Integration;

/// <summary>AC2: register, login, logout; refresh after logout is 401; access-token lifetime is 10 minutes.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Test-Password1!";
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
    public async Task Register_then_login_returns_tokens_under_api_auth_prefix()
    {
        var email = NewEmail();

        var register = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var tokens = await LoginAsync(email);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
    }

    [Fact]
    public async Task Register_twice_with_same_email_fails()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var second = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Password1!" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Refresh_works_before_logout()
    {
        var tokens = await RegisterAndLoginAsync();

        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_old_refresh_token_returns_401_after_logout()
    {
        var tokens = await RegisterAndLoginAsync();

        var logout = await SendAuthorizedAsync(HttpMethod.Post, "/api/auth/logout", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_without_token_returns_401()
    {
        var logout = await _client.PostAsync("/api/auth/logout", content: null, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
    }

    [Fact]
    public async Task Access_token_lifetime_is_10_minutes()
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>()
            .Get(IdentityConstants.BearerScheme);
        Assert.Equal(TimeSpan.FromMinutes(10), options.BearerTokenExpiration);

        var tokens = await RegisterAndLoginAsync();
        Assert.Equal(600, tokens.ExpiresIn);
    }

    [Fact]
    public async Task Access_token_issued_before_logout_still_works_until_it_expires()
    {
        // Documents the known limit of spec §7: only the refresh token is invalidated by logout.
        var tokens = await RegisterAndLoginAsync();

        var first = await SendAuthorizedAsync(HttpMethod.Post, "/api/auth/logout", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await SendAuthorizedAsync(HttpMethod.Post, "/api/auth/logout", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    public static TheoryData<string, string, string?> UnusedEndpoints => new()
    {
        // method, path, JSON body (null = no body). Partial and empty bodies must not reveal the endpoint (400).
        { "POST", "/api/auth/resendConfirmationEmail", "{\"email\":\"a@example.test\"}" },
        { "POST", "/api/auth/resendConfirmationEmail", null },
        { "POST", "/api/auth/forgotPassword", "{\"email\":\"a@example.test\"}" },
        { "POST", "/api/auth/forgotPassword", "{}" },
        { "POST", "/api/auth/resetPassword", "{\"email\":\"a@example.test\"}" },
        { "POST", "/api/auth/resetPassword", null },
        { "GET", "/api/auth/confirmEmail?userId=x&code=y", null },
        { "GET", "/api/auth/confirmEmail", null },
        { "GET", "/api/auth/manage/info", null },
        { "POST", "/api/auth/manage/info", "{\"newEmail\":\"b@example.test\"}" },
        { "POST", "/api/auth/manage/2fa", "{}" },
    };

    [Theory]
    [MemberData(nameof(UnusedEndpoints))]
    public async Task Unused_identity_endpoints_return_404_for_anonymous_callers(string method, string path,
        string? body)
    {
        var response = await _client.SendAsync(CreateRequest(method, path, body), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(UnusedEndpoints))]
    public async Task Unused_identity_endpoints_return_404_for_authenticated_callers(string method, string path,
        string? body)
    {
        var tokens = await RegisterAndLoginAsync();
        var request = CreateRequest(method, path, body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await _client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(string method, string path, string? body) =>
        new(new HttpMethod(method), path)
        {
            Content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    private async Task RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }, Ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<AccessTokenResponse> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccessTokenResponse>(Ct))!;
    }

    private async Task<AccessTokenResponse> RegisterAndLoginAsync()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        return await LoginAsync(email);
    }

    private Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, string accessToken,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync(request, Ct);
    }
}
