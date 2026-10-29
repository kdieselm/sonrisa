using System.Net;
using System.Net.Http.Json;
using System.Text;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.ValueObjects;

namespace Sonrisa.Infrastructure.Channels.Slack;

public sealed partial class SlackChannel
{
    /// <summary>
    /// Plain text with the same fields as the email (spec §2.6, T68). Slack's three control characters are escaped
    /// (<c>&amp; &lt; &gt;</c>), so feed text cannot create mentions or links.
    /// </summary>
    public FormattedMessage Format(NotificationContent content)
    {
        static string E(string? value) =>
            (value ?? string.Empty).ReplaceLineEndings(" ").Replace("&", "&amp;").Replace("<", "&lt;")
            .Replace(">", "&gt;");

        var text = new StringBuilder();
        if (content.IsSimulated)
        {
            text.AppendLine("*SIMULATED* – created by an administrator, it did not happen.");
        }

        text.AppendLine($"*{E(content.Title)}*");
        text.AppendLine($"{E(content.CategoryLabel)} · {E(content.KeyLabel)}: {E(content.KeyValue)}");
        text.AppendLine($"Source: {E(content.SourceName)}");
        if (IsWebUrl(content.Link))
        {
            text.AppendLine($"Link: {E(content.Link)}");
        }

        text.AppendLine($"Why: {E(content.WhyMatched)}");
        if (IsWebUrl(content.AlertUrl))
        {
            text.AppendLine($"Your alert: {E(content.AlertUrl)}");
        }

        if (content.Disclaimer is { Length: > 0 } disclaimer)
        {
            text.AppendLine($"_{E(disclaimer)}_");
        }

        return new FormattedMessage(null, text.ToString().TrimEnd());
    }

    /// <summary>
    /// POST <c>{"text": …}</c> to the webhook (spec §2.7, D13). 2xx = sent; 429, 5xx and network errors retry
    /// (<c>Retry-After</c> honoured); any other status is final. Redirects are not followed. The error text never holds
    /// the URL.
    /// </summary>
    public async Task<SendResult> SendAsync(string configJson, FormattedMessage message, CancellationToken ct)
    {
        string? url;
        try
        {
            url = Deserialize(configJson).WebhookUrl;
        }
        catch (System.Text.Json.JsonException)
        {
            url = null;
        }

        if (url is null || !IsAllowed(url))
        {
            return SendResult.Permanent("The Slack destination has no valid webhook URL.");
        }

        try
        {
            using var response = await http.PostAsJsonAsync(url, new { text = message.Body }, ct);
            return Classify(response);
        }
        catch (HttpRequestException ex)
        {
            // The message of an HttpRequestException can contain the host, never the path; the URL stays out anyway.
            return SendResult.Retryable($"Slack request failed ({ex.HttpRequestError}).");
        }
    }

    /// <summary>The status policy of D13, separate so the unit tests can pin every code.</summary>
    public static SendResult Classify(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            return SendResult.Sent();
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return SendResult.Retryable("Slack answered 429 (rate limited).", RetryAfter(response));
        }

        if (status >= 500)
        {
            return SendResult.Retryable($"Slack answered {status}.", RetryAfter(response));
        }

        return SendResult.Permanent($"Slack answered {status}.");
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta && delta >= TimeSpan.Zero)
        {
            return delta;
        }

        return header?.Date is { } date ? TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks)) : null;
    }

    private static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
