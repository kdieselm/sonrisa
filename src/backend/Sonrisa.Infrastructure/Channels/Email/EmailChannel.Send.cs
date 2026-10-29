using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.ValueObjects;

namespace Sonrisa.Infrastructure.Channels.Email;

public sealed partial class EmailChannel
{
    /// <summary>HTML body; every value from outside is encoded (an event title is feed content).</summary>
    public FormattedMessage Format(NotificationContent content)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var subject = (content.IsSimulated ? "[SIMULATED] " : string.Empty)
                      + $"{content.CategoryLabel}: {OneLine(content.Title)}";

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:560px\">");
        if (content.IsSimulated)
        {
            html.Append("<p style=\"background:#fff3cd;padding:6px 10px\"><strong>SIMULATED</strong> – "
                        + "this event was created by an administrator, it did not happen.</p>");
        }

        html.Append($"<h2 style=\"margin:0 0 8px\">{E(content.Title)}</h2>");
        html.Append($"<p style=\"margin:0 0 12px;color:#555\">{E(content.CategoryLabel)}</p>");
        html.Append("<table cellpadding=\"4\" style=\"border-collapse:collapse\">");
        html.Append($"<tr><td><strong>{E(content.KeyLabel)}</strong></td><td>{E(content.KeyValue)}</td></tr>");
        html.Append($"<tr><td><strong>Source</strong></td><td>{E(content.SourceName)}</td></tr>");
        if (IsWebUrl(content.Link))
        {
            html.Append($"<tr><td><strong>Link</strong></td><td><a href=\"{E(content.Link)}\">{E(content.Link)}</a>"
                        + "</td></tr>");
        }

        html.Append($"<tr><td><strong>Why</strong></td><td>{E(content.WhyMatched)}</td></tr>");
        if (IsWebUrl(content.AlertUrl))
        {
            html.Append($"<tr><td><strong>Your alert</strong></td><td><a href=\"{E(content.AlertUrl)}\">"
                        + $"{E(content.AlertUrl)}</a></td></tr>");
        }

        html.Append("</table>");
        if (content.Disclaimer is { Length: > 0 } disclaimer)
        {
            html.Append($"<p style=\"margin-top:16px;color:#777;font-size:12px\">{E(disclaimer)}</p>");
        }

        html.Append("</div>");
        return new FormattedMessage(subject, html.ToString());
    }

    public async Task<SendResult> SendAsync(string configJson, FormattedMessage message, CancellationToken ct)
    {
        MimeMessage mime;
        try
        {
            var address = Deserialize(configJson).Address;
            mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(options.FromName, options.From));
            mime.To.Add(MailboxAddress.Parse(address ?? string.Empty));
            mime.Subject = message.Subject ?? string.Empty;
            mime.Body = new BodyBuilder { HtmlBody = message.Body }.ToMessageBody();
        }
        catch (Exception ex) when (ex is FormatException or ParseException or System.Text.Json.JsonException)
        {
            return SendResult.Permanent("The destination address or the sender address is not valid.");
        }

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(options.Host, options.Port,
                options.UseStartTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None, ct);
            if (!string.IsNullOrEmpty(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password ?? string.Empty, ct);
            }

            await client.SendAsync(mime, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Classify(ex);
        }

        // The server has accepted the message. Closing the connection is courtesy: a failure here must not turn a
        // delivered mail into a retry, which would send it again (S4 review 1).
        try
        {
            using var quit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.DisconnectAsync(true, quit.Token);
        }
        catch (Exception)
        {
            // Ignored on purpose; the client is disposed below.
        }

        return SendResult.Sent();
    }

    /// <summary>
    /// SMTP 4xx replies, connection problems and protocol errors are worth another attempt; SMTP 5xx replies and
    /// authentication failures will not change by waiting (architecture §3.2). The text names the cause, never a
    /// credential.
    /// </summary>
    public static SendResult Classify(Exception ex) => ex switch
    {
        SmtpCommandException cmd when (int)cmd.StatusCode is >= 400 and < 500 =>
            SendResult.Retryable(string.Create(CultureInfo.InvariantCulture,
                $"SMTP {(int)cmd.StatusCode}: {Trim(cmd.Message)}")),
        SmtpCommandException cmd => SendResult.Permanent(string.Create(CultureInfo.InvariantCulture,
            $"SMTP {(int)cmd.StatusCode}: {Trim(cmd.Message)}")),
        AuthenticationException => SendResult.Permanent("SMTP authentication failed."),
        ProtocolException => SendResult.Retryable($"SMTP protocol error: {Trim(ex.Message)}"),
        SocketException or IOException or TimeoutException or ServiceNotConnectedException
            or ServiceNotAuthenticatedException => SendResult.Retryable($"SMTP connection problem: {Trim(ex.Message)}"),
        _ => SendResult.Retryable($"SMTP error ({ex.GetType().Name}): {Trim(ex.Message)}"),
    };

    private static string Trim(string text) => OneLine(text) is { Length: > 300 } t ? t[..300] : OneLine(text);

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
