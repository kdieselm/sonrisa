using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.BearerToken;

namespace Sonrisa.Tests.Integration;

/// <summary>Register, login and authorized calls shared by the integration tests.</summary>
public static class TestApi
{
    public const string Password = "Test-Password1!";

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    public static async Task RegisterAsync(this HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password }, Ct);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<AccessTokenResponse> LoginAsync(this HttpClient client, string email,
        string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccessTokenResponse>(Ct))!;
    }

    public static async Task<(string Email, AccessTokenResponse Tokens)> RegisterAndLoginAsync(
        this HttpClient client)
    {
        var email = NewEmail();
        await client.RegisterAsync(email);
        return (email, await client.LoginAsync(email));
    }

    public static Task<AccessTokenResponse> LoginAsAdminAsync(this HttpClient client) =>
        client.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    public static Task<HttpResponseMessage> SendAuthorizedAsync(this HttpClient client, HttpMethod method,
        string path, string? accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client.SendAsync(request, Ct);
    }
}
