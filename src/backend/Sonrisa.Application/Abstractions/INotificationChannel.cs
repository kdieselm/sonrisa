using System.Text.Json;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.ValueObjects;

namespace Sonrisa.Application.Abstractions;

/// <summary>
/// A notification channel (spec §2.6, §2.11, ADR-2): email, Slack, later others. Owns the shape of a destination's
/// config, validates it and decides what of it may leave the API. Core code finds a channel only through the channel
/// registry by <see cref="Key"/>. How to add one: <c>docs/how-to/add-a-channel.md</c>.
/// </summary>
/// <remarks>S3b: config. S4: formatting and sending.</remarks>
public interface INotificationChannel
{
    /// <summary>Stable key stored in <c>Destinations.ChannelKey</c>, for example <c>email</c>.</summary>
    string Key { get; }

    string DisplayName { get; }

    /// <summary>
    /// Validates a destination config sent by the user and returns it as JSON in the channel's own shape. Missing
    /// fields get the channel's defaults on create (spec §2.6: the account email, the local Slack mock); on update
    /// (<see cref="ChannelContext.StoredConfig"/> set) an empty secret keeps the stored value.
    /// </summary>
    DataValidationResult ValidateConfig(JsonElement config, ChannelContext context);

    /// <summary>
    /// The stored config as JSON that is safe in any API response (AC18): secrets are masked. Never throws on a
    /// config this channel wrote.
    /// </summary>
    string MaskConfig(string configJson);

    /// <summary>A config the create form starts with (JSON, already masked if it would hold a secret).</summary>
    string DefaultConfig(ChannelContext context);

    /// <summary>
    /// Turns the channel-neutral content into this channel's message (HTML email, plain-text Slack). Pure; the
    /// channel never looks at the category (AC11).
    /// </summary>
    FormattedMessage Format(NotificationContent content);

    /// <summary>
    /// One attempt to send <paramref name="message"/> to the destination described by <paramref name="configJson"/>
    /// (a config this channel wrote). Classifies the outcome instead of throwing for delivery problems; the error text
    /// holds no secret (not the webhook URL). Honours <paramref name="ct"/>: the caller's timeout.
    /// </summary>
    Task<SendResult> SendAsync(string configJson, FormattedMessage message, CancellationToken ct);
}

/// <summary>A message in a channel's format: an optional subject (email) and the body.</summary>
public sealed record FormattedMessage(string? Subject, string Body);

/// <summary>
/// What a channel may know when it validates a config: the owner, and on update the stored config, so a field the
/// client could only see masked can be kept when it is sent empty.
/// </summary>
public sealed record ChannelContext(string UserId, string AccountEmail, string? StoredConfig = null);
