using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Mapping;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

/// <summary>
/// A user's alerts (spec §2.2, §2.5): create, list, edit, enable/disable, delete, and the recent-matches list. Every
/// call is scoped to the caller's user ID; another user's alert, or linking another user's destination, is "not
/// found" and changes nothing (AC4). Rules are validated by the alert's category; this service never reads them.
/// </summary>
public sealed class AlertService(
    IAlertRepository alerts,
    IDestinationRepository destinations,
    IDeliveryRepository deliveries,
    IEventRepository events,
    ITransactionRunner transactions,
    CategoryRegistry categories,
    SourceRegistry sources,
    TimeProvider time)
{
    /// <summary>Keeps every per-user list within one page (no unbounded reads).</summary>
    public const int MaxPerUser = 50;

    /// <summary>Back-fill (spec §2.5, D11): at most 3 events of the last 24 h.</summary>
    public const int RecentMatchLimit = 3;

    public static readonly TimeSpan RecentMatchWindow = TimeSpan.FromHours(24);

    /// <summary>The back-fill reads at most this many recent events of the category, newest first.</summary>
    public const int RecentMatchScanLimit = 500;

    public async Task<PagedResult<AlertDto>> ListAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await alerts.ListForUserAsync(userId, page, pageSize, ct);
        return new PagedResult<AlertDto>(items.Select(a => a.ToDto(categories)).ToList(), page, pageSize, total);
    }

    public async Task<AlertDto?> GetAsync(long id, string userId, CancellationToken ct) =>
        (await alerts.GetForUserAsync(id, userId, ct))?.ToDto(categories);

    public async Task<ServiceResult<AlertDto>> CreateAsync(string userId, CreateAlertRequest request,
        CancellationToken ct)
    {
        var errors = new FieldErrors();
        var category = request.CategoryKey is null ? null : categories.Find(request.CategoryKey);
        if (category is null)
        {
            errors.Add("categoryKey", "Choose a known category.");
        }

        var name = ValidName(request.Name, errors);
        var rule = category is null ? null : ValidRule(category, request.Rule, errors);
        var destinationIds = ValidDestinationCount(request.DestinationIds, errors);
        if (await alerts.CountForUserAsync(userId, ct) >= MaxPerUser)
        {
            errors.Add("alert", $"You can have at most {MaxPerUser} alerts.");
        }

        if (errors.Any || category is null || name is null || rule is null || destinationIds is null)
        {
            return ServiceResult<AlertDto>.Invalid(errors.ToDictionary());
        }

        if (!await OwnsAllAsync(userId, destinationIds, ct))
        {
            return ServiceResult<AlertDto>.NotFound();
        }

        var created = new Alert(userId, name, category.Key, rule, destinationIds, time.GetUtcNow());
        await transactions.RunAsync(_ =>
        {
            alerts.Add(created);
            return Task.FromResult(0);
        }, ct);
        return ServiceResult<AlertDto>.Ok(created.ToDto(categories));
    }

    /// <summary>
    /// Replaces name, rule and destinations. A changed rule resets <see cref="Alert.ActiveSince"/> (spec §2.5); new
    /// destinations get matches from now on only.
    /// </summary>
    public async Task<ServiceResult<AlertDto>> UpdateAsync(long id, string userId, UpdateAlertRequest request,
        CancellationToken ct)
    {
        var existing = await alerts.GetForUserAsync(id, userId, ct);
        if (existing is null)
        {
            return ServiceResult<AlertDto>.NotFound();
        }

        var errors = new FieldErrors();
        var category = categories.Find(existing.CategoryKey);
        if (category is null)
        {
            errors.Add("categoryKey", "This category is no longer available.");
        }

        var name = ValidName(request.Name, errors);
        var rule = category is null ? null : ValidRule(category, request.Rule, errors);
        var destinationIds = ValidDestinationCount(request.DestinationIds, errors);
        if (errors.Any || name is null || rule is null || destinationIds is null)
        {
            return ServiceResult<AlertDto>.Invalid(errors.ToDictionary());
        }

        if (!await OwnsAllAsync(userId, destinationIds, ct))
        {
            return ServiceResult<AlertDto>.NotFound();
        }

        await transactions.RunAsync(_ =>
        {
            existing.Rename(name);
            existing.ChangeRule(rule, time.GetUtcNow());
            existing.SetDestinations(destinationIds);
            return Task.FromResult(0);
        }, ct);
        return ServiceResult<AlertDto>.Ok(existing.ToDto(categories));
    }

    /// <summary>Re-enabling resets <see cref="Alert.ActiveSince"/> (AC5). Null if not found.</summary>
    public async Task<AlertDto?> SetEnabledAsync(long id, string userId, bool enabled, CancellationToken ct)
    {
        var existing = await alerts.GetForUserAsync(id, userId, ct);
        if (existing is null)
        {
            return null;
        }

        await transactions.RunAsync(_ =>
        {
            if (enabled)
            {
                existing.Enable(time.GetUtcNow());
            }
            else
            {
                existing.Disable();
            }

            return Task.FromResult(0);
        }, ct);
        return existing.ToDto(categories);
    }

    /// <summary>
    /// Soft delete (D20): the alert no longer matches; its pending deliveries fail with "alert deleted"; its rows stay
    /// on the Notifications page. False if not found.
    /// </summary>
    public async Task<bool> DeleteAsync(long id, string userId, CancellationToken ct)
    {
        var existing = await alerts.GetForUserAsync(id, userId, ct);
        if (existing is null)
        {
            return false;
        }

        await transactions.RunAsync(async innerCt =>
        {
            existing.SoftDelete(time.GetUtcNow());
            return await deliveries.FailPendingForAlertAsync(id, DeliveryFailure.AlertDeleted, "alert deleted",
                innerCt);
        }, ct);
        return true;
    }

    /// <summary>
    /// Up to 3 already-ingested events of the last 24 h that match the alert's rule (spec §2.5, D11, AC14). Ignores
    /// "active since" and the age limit on purpose: it shows what the alert would have caught. Sends nothing and
    /// writes nothing. Null if not found.
    /// </summary>
    public async Task<RecentMatchesDto?> RecentMatchesAsync(long id, string userId, CancellationToken ct)
    {
        var alert = await alerts.GetForUserAsync(id, userId, ct);
        if (alert is null)
        {
            return null;
        }

        var category = categories.Find(alert.CategoryKey);
        if (category is null || !category.SupportsBackfill)
        {
            return new RecentMatchesDto(false,
                category?.BackfillUnavailableReason ?? "This category is no longer available.", []);
        }

        var recent = await events.ListRecentAsync(category.Key, time.GetUtcNow() - RecentMatchWindow,
            RecentMatchScanLimit, ct);
        var items = recent
            .Where(e => EventIngestor.TryMatch(category, e, alert.Rule) == true)
            .Take(RecentMatchLimit)
            .Select(e => new RecentMatchDto(e.Id, e.Title, e.Url, sources.DisplayNameOf(e.SourceKey), e.PublishedAt,
                e.FirstSeenAt, e.IsSimulated, DtoMapping.Json(e.Data)))
            .ToList();
        return new RecentMatchesDto(true, null, items);
    }

    private async Task<bool> OwnsAllAsync(string userId, IReadOnlyList<long> ids, CancellationToken ct) =>
        (await destinations.FindOwnedAsync(userId, ids, ct)).Count == ids.Count;

    private static string? ValidRule(IEventCategory category, JsonElement rule, FieldErrors errors)
    {
        var result = category.ValidateRule(rule);
        foreach (var (field, messages) in result.Errors)
        {
            foreach (var message in messages)
            {
                errors.Add(field == "rule" ? field : $"rule.{field}", message);
            }
        }

        return result.DataJson;
    }

    private static IReadOnlyList<long>? ValidDestinationCount(IReadOnlyList<long>? ids, FieldErrors errors)
    {
        var distinct = (ids ?? []).Distinct().ToList();
        if (distinct.Count is 0 or > Alert.MaxDestinations)
        {
            errors.Add("destinationIds", $"Choose 1 to {Alert.MaxDestinations} destinations.");
            return null;
        }

        return distinct;
    }

    private static string? ValidName(string? name, FieldErrors errors)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > Alert.MaxNameLength)
        {
            errors.Add("name", $"Name is required, at most {Alert.MaxNameLength} characters.");
            return null;
        }

        return trimmed;
    }
}
