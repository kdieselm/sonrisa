using System.Security.Cryptography;
using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Channels.Slack;

/// <summary>
/// Slack incoming webhook (spec §2.6, T63, T70). Config: the webhook URL, a secret. It is masked in every response
/// (only the last 4 characters), never logged and never audited. A new destination defaults to the local mock
/// receiver. Sending is in SlackChannel.Send.cs.
/// </summary>
public sealed partial class SlackChannel(SlackOptions options, HttpClient http) : INotificationChannel
{
    public const string ChannelKey = "slack";
    public const int MaxUrlLength = 500;
    private const string SlackHost = "hooks.slack.com";

    public string Key => ChannelKey;
    public string DisplayName => "Slack";

    public DataValidationResult ValidateConfig(JsonElement config, ChannelContext context)
    {
        var errors = new FieldErrors();
        var input = errors.Read<SlackConfig>(config, "config");
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        var url = string.IsNullOrWhiteSpace(input.WebhookUrl)
            ? StoredUrl(context) ?? NewMockUrl()
            : input.WebhookUrl.Trim();
        if (!IsAllowed(url))
        {
            errors.Add("webhookUrl", $"Use a Slack incoming webhook URL (https://{SlackHost}/…) or the local mock.");
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        return DataValidationResult.Valid(Serialize(new SlackConfig(url)));
    }

    /// <summary>Only the last 4 characters of the URL leave the API (AC18).</summary>
    public string MaskConfig(string configJson)
    {
        string? url;
        try
        {
            url = Deserialize(configJson).WebhookUrl;
        }
        catch (JsonException)
        {
            url = null;
        }

        return Serialize(new SlackConfig(Mask(url)));
    }

    /// <summary>The form starts empty: an empty URL means "a new mock receiver URL" on create.</summary>
    public string DefaultConfig(ChannelContext context) => Serialize(new SlackConfig(null));

    public static string? Mask(string? url) =>
        string.IsNullOrEmpty(url) ? null : "…" + url[^Math.Min(4, url.Length)..];

    public static SlackConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<SlackConfig>(json, JsonText.Options)
        ?? throw new JsonException("Slack config is null.");

    /// <summary>
    /// A real webhook (<c>https://hooks.slack.com/…</c>) or a URL under the configured mock base (spec §2.6, T69).
    /// </summary>
    public bool IsAllowed(string url)
    {
        if (url.Length > MaxUrlLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
            && string.Equals(uri.Host, SlackHost, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Length > 1 && string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        // The mock: same scheme, host and port as the configured base, and a path below it. Escapes and dot
        // segments are refused outright, so the sender can never be steered to another path of the API.
        if (!Uri.TryCreate(options.MockBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var mockBase)
            || url.Contains('%') || url.Contains("..", StringComparison.Ordinal) || url.Contains('\\')
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.Scheme == mockBase.Scheme
               && string.Equals(uri.Host, mockBase.Host, StringComparison.OrdinalIgnoreCase)
               && uri.Port == mockBase.Port
               && uri.AbsolutePath.StartsWith(mockBase.AbsolutePath, StringComparison.Ordinal)
               && uri.AbsolutePath.Length > mockBase.AbsolutePath.Length;
    }

    /// <summary>On update: the stored URL, kept when the client sends the field empty (it only saw it masked).</summary>
    private static string? StoredUrl(ChannelContext context)
    {
        if (context.StoredConfig is null)
        {
            return null;
        }

        try
        {
            return Deserialize(context.StoredConfig).WebhookUrl;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A fresh hook name, like a real webhook's unique path.</summary>
    private string NewMockUrl() =>
        $"{options.MockBaseUrl.TrimEnd('/')}/hooks/{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";

    private static string Serialize(SlackConfig config) => JsonSerializer.Serialize(config, JsonText.Options);
}

public sealed record SlackConfig(string? WebhookUrl);
