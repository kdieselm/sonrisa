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

    public async Task<IReadOnlyList<long>> ListDueIdsAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        await db.Set<Delivery>().AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id)
            .Select(d => d.Id)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<DeliveryWork?> GetWorkAsync(long id, CancellationToken ct)
    {
        var delivery = await db.Set<Delivery>().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
        {
            return null;
        }

        var alert = await db.Set<Alert>().AsNoTracking().FirstAsync(a => a.Id == delivery.AlertId, ct);
        var destination = await db.Set<Destination>().AsNoTracking()
            .FirstAsync(d => d.Id == delivery.DestinationId, ct);
        var item = await db.Set<Event>().AsNoTracking().FirstAsync(e => e.Id == delivery.EventId, ct);
        return new DeliveryWork(delivery, alert, destination, item);
    }

    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row changed under us (xmin token). Drop our stale copy; the other writer's state stands.
            db.ChangeTracker.Clear();
            return false;
        }
    }

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

    public async Task<(IReadOnlyList<AdminDeliveryRow> Items, int Total)> ListForAdminAsync(DeliveryStatus? status,
        int page, int pageSize, CancellationToken ct)
    {
        var query =
            from d in db.Set<Delivery>().AsNoTracking()
            join a in db.Set<Alert>() on d.AlertId equals a.Id
            join e in db.Set<Event>() on d.EventId equals e.Id
            join dest in db.Set<Destination>() on d.DestinationId equals dest.Id
            where status == null || d.Status == status
            orderby d.CreatedAt descending, d.Id descending
            select new AdminDeliveryRow(d.Id, d.CreatedAt, a.UserId, a.Id, a.Name, e.Id, e.Title, e.IsSimulated,
                dest.ChannelKey, dest.DisplayName, d.Status, d.Attempts, d.NextAttemptAt, d.SentAt, d.Failure,
                d.LastError);
        var total = await query.CountAsync(ct);
        var items = await query.Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync(ct);
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
