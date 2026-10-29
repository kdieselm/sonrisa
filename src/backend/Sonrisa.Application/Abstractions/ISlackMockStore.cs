namespace Sonrisa.Application.Abstractions;

/// <summary>
/// Messages received by the local Slack mock receiver (spec §2.6, §2.9). The receiver writes, the admin view reads.
/// </summary>
public interface ISlackMockStore
{
    Task AddAsync(string hookName, string body, CancellationToken ct);

    /// <summary>Newest first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<SlackMockMessageRecord> Items, int Total)> ListAsync(int page, int pageSize,
        CancellationToken ct);
}

public sealed record SlackMockMessageRecord(long Id, string HookName, string Body, DateTimeOffset ReceivedAt);
