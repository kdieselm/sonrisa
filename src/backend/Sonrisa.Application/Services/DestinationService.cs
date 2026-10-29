using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Mapping;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Destinations;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

/// <summary>
/// A user's destinations (spec §2.6). Every call is scoped to the caller's user ID; another user's destination is
/// "not found" (AC4). Configs are validated by the channel on write and masked on read (AC18).
/// </summary>
public sealed class DestinationService(
    IDestinationRepository destinations,
    IAlertRepository alerts,
    IDeliveryRepository deliveries,
    IUserDirectory users,
    ITransactionRunner transactions,
    ChannelRegistry channels,
    TimeProvider time)
{
    /// <summary>Keeps every per-user list within one page (no unbounded reads).</summary>
    public const int MaxPerUser = 20;

    public async Task<PagedResult<DestinationDto>> ListAsync(string userId, int page, int pageSize,
        CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await destinations.ListForUserAsync(userId, page, pageSize, ct);
        return new PagedResult<DestinationDto>(items.Select(d => d.ToDto(channels)).ToList(), page, pageSize, total);
    }

    public async Task<DestinationDto?> GetAsync(long id, string userId, CancellationToken ct) =>
        (await destinations.GetForUserAsync(id, userId, ct))?.ToDto(channels);

    public async Task<ServiceResult<DestinationDto>> CreateAsync(string userId, CreateDestinationRequest request,
        CancellationToken ct)
    {
        var errors = new FieldErrors();
        var channel = request.ChannelKey is null ? null : channels.Find(request.ChannelKey);
        if (channel is null)
        {
            errors.Add("channelKey", "Choose a known channel.");
        }

        var name = ValidName(request.DisplayName, errors);
        if (await destinations.CountForUserAsync(userId, ct) >= MaxPerUser)
        {
            errors.Add("destination", $"You can have at most {MaxPerUser} destinations.");
        }

        string? config = null;
        if (channel is not null)
        {
            config = Validate(channel, request.Config, await ContextAsync(userId, ct), errors);
        }

        if (errors.Any || channel is null || name is null || config is null)
        {
            return ServiceResult<DestinationDto>.Invalid(errors.ToDictionary());
        }

        var created = new Destination(userId, channel.Key, name, config, time.GetUtcNow());
        await transactions.RunAsync(_ =>
        {
            destinations.Add(created);
            return Task.FromResult(0);
        }, ct);
        return ServiceResult<DestinationDto>.Ok(created.ToDto(channels));
    }

    /// <summary>A missing config keeps the stored one (the client only ever saw it masked).</summary>
    public async Task<ServiceResult<DestinationDto>> UpdateAsync(long id, string userId,
        UpdateDestinationRequest request, CancellationToken ct)
    {
        var existing = await destinations.GetForUserAsync(id, userId, ct);
        if (existing is null)
        {
            return ServiceResult<DestinationDto>.NotFound();
        }

        var errors = new FieldErrors();
        var name = ValidName(request.DisplayName, errors);
        var config = existing.Config;
        if (request.Config is { } sent)
        {
            var channel = channels.Find(existing.ChannelKey);
            config = channel is null
                ? null
                : Validate(channel, sent, await ContextAsync(userId, ct) with { StoredConfig = existing.Config },
                    errors);
            if (channel is null)
            {
                errors.Add("channelKey", "This channel is no longer available.");
            }
        }

        if (errors.Any || name is null || config is null)
        {
            return ServiceResult<DestinationDto>.Invalid(errors.ToDictionary());
        }

        await transactions.RunAsync(_ =>
        {
            existing.Update(name, config);
            return Task.FromResult(0);
        }, ct);
        return ServiceResult<DestinationDto>.Ok(existing.ToDto(channels));
    }

    /// <summary>
    /// Soft delete (spec §2.6): unlinked from the caller's alerts; its pending deliveries fail with
    /// "destination deleted"; history stays. False if not found.
    /// </summary>
    public async Task<bool> DeleteAsync(long id, string userId, CancellationToken ct)
    {
        var existing = await destinations.GetForUserAsync(id, userId, ct);
        if (existing is null)
        {
            return false;
        }

        await transactions.RunAsync(async innerCt =>
        {
            existing.SoftDelete(time.GetUtcNow());
            foreach (var alert in await alerts.ListLinkedToAsync(id, userId, innerCt))
            {
                alert.Unlink(id);
            }

            return await deliveries.FailPendingForDestinationAsync(id, DeliveryFailure.DestinationDeleted,
                "destination deleted", innerCt);
        }, ct);
        return true;
    }

    /// <summary>What a channel may know about the caller (the account email is the email default, spec §2.6).</summary>
    public async Task<ChannelContext> ContextAsync(string userId, CancellationToken ct)
    {
        var emails = await users.GetEmailsAsync([userId], ct);
        return new ChannelContext(userId, emails.GetValueOrDefault(userId) ?? string.Empty);
    }

    private static string? Validate(INotificationChannel channel, JsonElement? config, ChannelContext context,
        FieldErrors errors)
    {
        using var empty = JsonDocument.Parse("{}");
        var result = channel.ValidateConfig(config ?? empty.RootElement, context);
        foreach (var (field, messages) in result.Errors)
        {
            foreach (var message in messages)
            {
                errors.Add(field == "config" ? field : $"config.{field}", message);
            }
        }

        return result.DataJson;
    }

    private static string? ValidName(string? name, FieldErrors errors)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > Destination.MaxDisplayNameLength)
        {
            errors.Add("displayName", $"Name is required, at most {Destination.MaxDisplayNameLength} characters.");
            return null;
        }

        return trimmed;
    }
}
