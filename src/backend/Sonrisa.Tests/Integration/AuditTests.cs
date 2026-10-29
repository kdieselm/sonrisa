using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>AC12 (auth part): register, login and logout each write exactly one entry; no secrets; admin only.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Register_login_logout_write_one_entry_each()
    {
        var email = NewEmail();
        await _client.RegisterAsync(email);
        var userId = await UserIdAsync(email);

        var afterRegister = await EntriesForTargetAsync(userId);
        var register = Assert.Single(afterRegister);
        Assert.Equal((AuditActions.Register, AuditOutcome.Success, userId), (register.Action, register.Outcome, register.ActorUserId));

        var tokens = await _client.LoginAsync(email);
        var login = Assert.Single(await EntriesForTargetAsync(userId, AuditActions.Login));
        Assert.Equal((AuditOutcome.Success, userId), (login.Outcome, login.ActorUserId));

        var logout = await _client.SendAuthorizedAsync(HttpMethod.Post, "/api/auth/logout", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var logoutEntry = Assert.Single(await EntriesForTargetAsync(userId, AuditActions.Logout));
        Assert.Equal((AuditOutcome.Success, userId), (logoutEntry.Outcome, logoutEntry.ActorUserId));

        Assert.Equal(3, (await EntriesForTargetAsync(userId)).Count);
    }

    [Fact]
    public async Task Failed_login_writes_a_failure_without_an_actor()
    {
        var email = NewEmail();
        await _client.RegisterAsync(email);
        var userId = await UserIdAsync(email);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Password1!" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var entry = Assert.Single(await EntriesForTargetAsync(userId, AuditActions.Login));
        Assert.Equal(AuditOutcome.Failure, entry.Outcome);
        Assert.Null(entry.ActorUserId);
    }

    [Fact]
    public async Task Failed_login_with_unknown_email_writes_one_failure()
    {
        var before = await CountAsync(e => e.Action == AuditActions.Login && e.TargetId == null
            && e.Outcome == AuditOutcome.Failure);

        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = NewEmail(), password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var after = await CountAsync(e => e.Action == AuditActions.Login && e.TargetId == null
            && e.Outcome == AuditOutcome.Failure);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task Failed_register_for_an_existing_email_writes_a_failure_without_an_actor()
    {
        var email = NewEmail();
        await _client.RegisterAsync(email);
        var userId = await UserIdAsync(email);

        var again = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        var entries = await EntriesForTargetAsync(userId, AuditActions.Register);
        Assert.Equal(2, entries.Count);
        var failure = Assert.Single(entries, e => e.Outcome == AuditOutcome.Failure);
        Assert.Null(failure.ActorUserId);
    }

    [Fact]
    public async Task Login_of_a_locked_out_user_is_a_failure_even_with_the_right_password()
    {
        var email = NewEmail();
        await _client.RegisterAsync(email);
        var userId = await UserIdAsync(email);

        // Identity's default lockout: 5 failed attempts.
        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Password1!" }, Ct);
        }

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var entries = await EntriesForTargetAsync(userId, AuditActions.Login);
        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.Equal(AuditOutcome.Failure, e.Outcome));
    }

    [Fact]
    public async Task Huge_page_number_returns_an_empty_page_not_an_error()
    {
        var admin = await _client.LoginAsAdminAsync();

        var response = await _client.SendAuthorizedAsync(HttpMethod.Get,
            $"/api/admin/audit?page={int.MaxValue}&pageSize=100", admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<AuditEntryDto>>(Ct))!;
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Refresh_writes_no_entry()
    {
        var (email, tokens) = await _client.RegisterAndLoginAsync();
        var userId = await UserIdAsync(email);
        var before = (await EntriesForTargetAsync(userId)).Count;

        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        Assert.Equal(before, (await EntriesForTargetAsync(userId)).Count);
    }

    [Fact]
    public async Task No_entry_contains_a_password_email_or_token()
    {
        var (email, tokens) = await _client.RegisterAndLoginAsync();
        await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Password1!" }, Ct);
        await _client.SendAuthorizedAsync(HttpMethod.Post, "/api/auth/logout", tokens.AccessToken);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var values = await db.Set<AuditEntry>()
            .Select(e => new[] { e.ActorUserId, e.Action, e.TargetType, e.TargetId })
            .ToListAsync(Ct);

        var secrets = new[] { Password, "Wrong-Password1!", email, tokens.AccessToken, tokens.RefreshToken };
        foreach (var value in values.SelectMany(v => v).OfType<string>())
        {
            Assert.DoesNotContain(secrets, s => value.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Normal_user_gets_403_on_the_audit_endpoint()
    {
        var (_, tokens) = await _client.RegisterAndLoginAsync();

        var response = await _client.SendAuthorizedAsync(HttpMethod.Get, "/api/admin/audit", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_sees_the_entries_newest_first_with_actor_email()
    {
        var (email, _) = await _client.RegisterAndLoginAsync();
        var admin = await _client.LoginAsAdminAsync();

        var response = await _client.SendAuthorizedAsync(HttpMethod.Get, "/api/admin/audit?page=1&pageSize=500",
            admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<AuditEntryDto>>(Ct))!;

        Assert.Equal(100, page.PageSize); // capped
        Assert.True(page.Total >= page.Items.Count);
        Assert.Equal(page.Items.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Select(e => e.Id),
            page.Items.Select(e => e.Id));
        Assert.Contains(page.Items, e => e.Action == AuditActions.Register && e.ActorEmail == email
            && e.Outcome == nameof(AuditOutcome.Success));
    }

    [Fact]
    public async Task Admin_user_list_contains_new_user_and_admin_role()
    {
        var (email, _) = await _client.RegisterAndLoginAsync();
        var admin = await _client.LoginAsAdminAsync();

        // Walk every page: the shared test database has more users than one page.
        var users = new List<AdminUserDto>();
        for (var page = 1; ; page++)
        {
            var result = await GetUsersPageAsync(admin.AccessToken, page, 20);
            users.AddRange(result.Items);
            if (page * result.PageSize >= result.Total)
            {
                Assert.Equal(result.Total, users.Count);
                break;
            }
        }

        Assert.Empty(Assert.Single(users, u => u.Email == email).Roles);
        Assert.Equal(["Admin"], Assert.Single(users, u => u.Email == ApiFactory.AdminEmail).Roles);
    }

    [Fact]
    public async Task Admin_user_list_is_paged_ordered_and_capped()
    {
        // Enough users for at least two pages of 2, whatever else is in the database.
        for (var i = 0; i < 3; i++)
        {
            await _client.RegisterAsync(NewEmail());
        }

        var admin = await _client.LoginAsAdminAsync();

        var first = await GetUsersPageAsync(admin.AccessToken, 1, 2);
        var second = await GetUsersPageAsync(admin.AccessToken, 2, 2);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(first.Total, second.Total);
        Assert.Empty(first.Items.Select(u => u.Id).Intersect(second.Items.Select(u => u.Id)));
        var emails = first.Items.Concat(second.Items).Select(u => u.Email).ToList();
        Assert.Equal(emails.Order(StringComparer.Ordinal), emails);

        var capped = await GetUsersPageAsync(admin.AccessToken, 1, 5000);
        Assert.Equal(100, capped.PageSize);
        Assert.True(capped.Items.Count <= 100);

        var pastTheEnd = await GetUsersPageAsync(admin.AccessToken, int.MaxValue, 100);
        Assert.Empty(pastTheEnd.Items);
    }

    [Fact]
    public async Task Email_lookup_rejects_more_than_one_page_of_ids()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IUserDirectory>();

        var tooMany = Enumerable.Range(0, 101).Select(i => $"id-{i}").ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => directory.GetEmailsAsync(tooMany, Ct));

        var oneUser = (await _client.RegisterAndLoginAsync()).Email;
        var id = await UserIdAsync(oneUser);
        var found = await directory.GetEmailsAsync([.. tooMany.Take(99), id], Ct);
        Assert.Equal(oneUser, Assert.Single(found).Value);
    }

    private async Task<PagedResult<AdminUserDto>> GetUsersPageAsync(string accessToken, int page, int pageSize)
    {
        var response = await _client.SendAuthorizedAsync(HttpMethod.Get,
            $"/api/admin/users?page={page}&pageSize={pageSize}", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<AdminUserDto>>(Ct))!;
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(Ct);
    }

    private async Task<List<AuditEntry>> EntriesForTargetAsync(string userId, string? action = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<AuditEntry>()
            .Where(e => e.TargetId == userId && (action == null || e.Action == action))
            .ToListAsync(Ct);
    }

    private async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<AuditEntry, bool>> predicate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<AuditEntry>().CountAsync(predicate, Ct);
    }
}
