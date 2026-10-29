namespace Sonrisa.Application.Abstractions;

/// <summary>Read-only access to user accounts. The Identity user type lives in Infrastructure (architecture §3.3).</summary>
public interface IUserDirectory
{
    /// <summary>One page of users with their roles, ordered by email. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<UserSummary> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Emails of the given user IDs (unknown IDs are left out). Takes at most <see cref="Paging.MaxPageSize"/> IDs:
    /// it resolves the actors of one list page, never a whole table.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task<string?> FindIdByEmailAsync(string email, CancellationToken ct);
}

public sealed record UserSummary(string Id, string Email, IReadOnlyList<string> Roles);
