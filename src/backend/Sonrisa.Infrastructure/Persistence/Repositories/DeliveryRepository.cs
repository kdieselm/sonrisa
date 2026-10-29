using Microsoft.EntityFrameworkCore;
using Sonrisa.Application;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Repositories;

public sealed class DeliveryRepository(AppDbContext db) : IDeliveryRepository
{
    public void Add(Delivery delivery) => db.Set<Delivery>().Add(delivery);

    public Task<int> FailPendingForAlertAsync(long alertId, DeliveryFailure failure, string error,
        CancellationToken ct) =>
        FailPending(db.Set<Delivery>().Where(d => d.AlertId == alertId), failure, error, ct);

    public Task<int> FailPendingForDestinationAsync(long destinationId, DeliveryFailure failure, string error,
        CancellationToken ct) =>
        FailPending(db.Set<Delivery>().Where(d => d.DestinationId == destinationId), failure, error, ct);

    public async Task<(IReadOnlyList<NotificationRow> Items, int Total)> ListForUserAsync(string userId, int page,
        int pageSize, CancellationToken ct)
    {
        var query = Rows(userId, null);
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<NotificationRow?> GetForUserAsync(long id, string userId, CancellationToken ct) =>
        Rows(userId, id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// One set-based update, the same transition as <see cref="Delivery.Fail"/>: only <c>Pending</c> rows change.
    /// Runs inside the caller's transaction.
    /// </summary>
    private static Task<int> FailPending(IQueryable<Delivery> rows, DeliveryFailure failure, string error,
        CancellationToken ct) =>
        rows.Where(d => d.Status == DeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DeliveryStatus.Failed)
                .SetProperty(d => d.Failure, failure)
                .SetProperty(d => d.LastError, error)
                .SetProperty(d => d.NextAttemptAt, (DateTimeOffset?)null), ct);

    /// <summary>
    /// The caller's rows: owner = the alert's owner (spec §2.12). Deleted alerts and destinations still show, so the
    /// history stays visible. Newest first. Projects only display fields: no config, no error text. Filters and
    /// ordering come before the projection, which EF Core cannot look into.
    /// </summary>
    private IQueryable<NotificationRow> Rows(string userId, long? id) =>
        from d in db.Set<Delivery>().AsNoTracking()
        join a in db.Set<Alert>() on d.AlertId equals a.Id
        join e in db.Set<Event>() on d.EventId equals e.Id
        join dest in db.Set<Destination>() on d.DestinationId equals dest.Id
        where a.UserId == userId && (id == null || d.Id == id)
        orderby d.CreatedAt descending, d.Id descending
        select new NotificationRow(
            d.Id,
            d.CreatedAt,
            a.Id,
            a.Name,
            a.DeletedAt != null,
            e.Id,
            e.Title,
            e.Url,
            e.IsSimulated,
            dest.ChannelKey,
            dest.DisplayName,
            dest.DeletedAt != null,
            d.Status,
            d.Failure,
            d.Attempts,
            d.SentAt);
}
