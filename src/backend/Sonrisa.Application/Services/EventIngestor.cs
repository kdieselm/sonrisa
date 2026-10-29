using System.Security.Cryptography;
using System.Text;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

public enum IngestOutcome
{
    Inserted,
    Updated,
    Unchanged,
}

/// <param name="DeliveriesCreated">Pending delivery rows created by matching (0 for a revision).</param>
/// <param name="FailedRuleAlertIds">Alerts whose rule could not be evaluated; skipped, the caller logs them.</param>
public sealed record IngestResult(IngestOutcome Outcome, Event Event, int DeliveriesCreated = 0,
    IReadOnlyList<long>? FailedRuleAlertIds = null);

/// <summary>
/// Stores one incoming event and matches it, in one transaction (architecture §3.1, ADR-3). Known by its external ID
/// or by one of its alternate IDs → update the row only (revision, T9), never matched again; otherwise insert, and
/// if the event is within the category's age limit, match it against the enabled alerts of its category that were
/// active before it was first seen, and queue one <see cref="DeliveryStatus.Pending"/> delivery per linked
/// destination. Used by the poller and by the admin "simulate event" form alike (T14).
/// </summary>
public sealed class EventIngestor(
    IEventRepository events,
    IAlertRepository alerts,
    IDeliveryRepository deliveries,
    CategoryRegistry categories,
    ITransactionRunner transactions,
    TimeProvider time)
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
                var (count, failed) = await MatchAsync(created, now, innerCt);
                return new IngestResult(IngestOutcome.Inserted, created, count, failed);
            }

            // The stored external ID stays as first seen, so later polls keep finding the row. A revision is never
            // matched again, so it never causes a second notification (spec §2.3, AC16).
            var changed = existing.Revise(title, summary, url, item.PublishedAt ?? existing.PublishedAt,
                item.DataJson, now);
            return new IngestResult(changed ? IngestOutcome.Updated : IngestOutcome.Unchanged, existing);
        }, ct);

        try
        {
            return await Once();
        }
        catch (DuplicateKeyException ex) when (ex.ConstraintName is null or EventIndexName)
        {
            // Another writer inserted the same (source, external ID) between our read and our insert. The transaction
            // was rolled back with its deliveries, so the second run finds the row and treats the item as a revision.
            return await Once();
        }
    }

    /// <summary>
    /// The unique index on (source, external ID). Only its violation is a lost insert race; any other unique error
    /// (a delivery row) is a bug and is rethrown instead of being retried as a revision.
    /// </summary>
    public const string EventIndexName = "IX_Events_SourceKey_ExternalId";

    /// <summary>
    /// Too old (published more than the category's <see cref="IEventCategory.MaxAge"/> before it was first seen,
    /// spec §2.5, T34): stored and listed, but never notified.
    /// </summary>
    public static bool IsTooOld(IEventCategory category, Event item) =>
        category.MaxAge is { } maxAge && item.PublishedAt < item.FirstSeenAt - maxAge;

    /// <summary>
    /// Evaluates a stored rule; a rule or event the category cannot read counts as no match (null), so one broken
    /// alert never blocks the event or the other alerts.
    /// </summary>
    public static bool? TryMatch(IEventCategory category, Event item, string rule)
    {
        try
        {
            return category.Matches(item, rule);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<(int Count, IReadOnlyList<long> Failed)> MatchAsync(Event created, DateTimeOffset now,
        CancellationToken ct)
    {
        var category = categories.Find(created.CategoryKey);
        if (category is null || IsTooOld(category, created))
        {
            return (0, []);
        }

        var count = 0;
        var failed = new List<long>();
        foreach (var candidate in await alerts.ListMatchCandidatesAsync(category.Key, created.FirstSeenAt, ct))
        {
            switch (TryMatch(category, created, candidate.Rule))
            {
                case null:
                    failed.Add(candidate.AlertId);
                    break;
                case true:
                    foreach (var destinationId in candidate.DestinationIds)
                    {
                        deliveries.Add(new Delivery(candidate.AlertId, created, destinationId, now));
                        count++;
                    }

                    break;
            }
        }

        return (count, failed);
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
