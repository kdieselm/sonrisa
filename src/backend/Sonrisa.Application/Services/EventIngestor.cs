using System.Security.Cryptography;
using System.Text;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

public enum IngestOutcome
{
    Inserted,
    Updated,
    Unchanged,
}

public sealed record IngestResult(IngestOutcome Outcome, Event Event);

/// <summary>
/// Stores one incoming event in its own transaction (architecture §3.1, ADR-3). Known by its external ID or by one
/// of its alternate IDs → update the row only (revision, T9), never treated as new; otherwise insert. Used by the
/// poller and by the admin "simulate event" form alike (T14). Matching is added inside the same transaction in S3b.
/// </summary>
public sealed class EventIngestor(IEventRepository events, ITransactionRunner transactions, TimeProvider time)
{
    public async Task<IngestResult> IngestAsync(string sourceKey, string categoryKey, IncomingEvent item,
        bool isSimulated, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ExternalId);
        var externalId = StorableId(item.ExternalId);
        IReadOnlyCollection<string> knownIds = (item.AlternateIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(StorableId)
            .Prepend(externalId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var title = Truncate(item.Title.Trim(), Event.MaxTitleLength);
        var summary = item.Summary is null ? null : Truncate(item.Summary.Trim(), Event.MaxSummaryLength);
        var url = item.Url is { Length: <= Event.MaxUrlLength } ? item.Url : null;

        Task<IngestResult> Once() => transactions.RunAsync(async innerCt =>
        {
            var now = time.GetUtcNow();
            var existing = await events.FindAsync(sourceKey, knownIds, innerCt);
            if (existing is null)
            {
                var created = new Event(categoryKey, sourceKey, externalId, title, summary, url,
                    item.PublishedAt ?? now, now, isSimulated, item.DataJson);
                events.Add(created);
                return new IngestResult(IngestOutcome.Inserted, created);
            }

            // The stored external ID stays as first seen, so later polls keep finding the row.
            var changed = existing.Revise(title, summary, url, item.PublishedAt ?? existing.PublishedAt,
                item.DataJson, now);
            return new IngestResult(changed ? IngestOutcome.Updated : IngestOutcome.Unchanged, existing);
        }, ct);

        try
        {
            return await Once();
        }
        catch (DuplicateKeyException)
        {
            // Another writer inserted the same (source, external ID) between our read and our insert. The transaction
            // was rolled back, so the second run finds the row and treats the item as a revision.
            return await Once();
        }
    }

    /// <summary>
    /// External IDs longer than the column (a long RSS link used as ID) are replaced by a stable SHA-256 hash, so the
    /// same item still maps to the same row instead of failing on every poll.
    /// </summary>
    public static string StorableId(string id) =>
        id.Length <= Event.MaxExternalIdLength
            ? id
            : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
