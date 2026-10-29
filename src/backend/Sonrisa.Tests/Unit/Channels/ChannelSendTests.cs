using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.ValueObjects;
using Sonrisa.Infrastructure.Channels.Email;
using Sonrisa.Infrastructure.Channels.Slack;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Tests.Unit.Channels;

/// <summary>
/// Spec §2.6, §2.7, D13, AC19: what the channels put into a message and how they classify a send attempt.
/// </summary>
public sealed class ChannelSendTests
{
    private const string Hook = "https://hooks.slack.com/services/T000/B000/SECRETSECRET1234";

    private static readonly NotificationContent Content = new(
        Title: "Quake <b>in</b> Japan & more",
        CategoryLabel: "Natural disaster",
        KeyLabel: "Magnitude",
        KeyValue: "M 6.1 – Japan",
        SourceName: "USGS earthquakes",
        Link: "https://example.test/quake?a=1&b=2",
        IsSimulated: false,
        WhyMatched: "Alert \"Japan\": magnitude at least 5.0.",
        AlertPath: "/alerts/7",
        Disclaimer: "Not an official warning.")
    {
        AlertUrl = "http://localhost:8080/alerts/7",
    };

    private static readonly SlackChannel Slack = new(new SlackOptions(), new HttpClient(new StubHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK))));

    private static readonly EmailChannel Email = new(new EmailOptions());

    // ---- Slack: format -----------------------------------------------------------------------------------------

    [Fact]
    public void Slack_message_has_every_field_of_the_email()
    {
        var text = Slack.Format(Content).Body;

        Assert.Contains("Natural disaster", text);
        Assert.Contains("Magnitude: M 6.1 – Japan", text);
        Assert.Contains("Source: USGS earthquakes", text);
        Assert.Contains("Link: https://example.test/quake?a=1&amp;b=2", text);
        Assert.Contains("Why: Alert \"Japan\": magnitude at least 5.0.", text);
        Assert.Contains("Your alert: http://localhost:8080/alerts/7", text);
        Assert.Contains("Not an official warning.", text);
        Assert.DoesNotContain("SIMULATED", text);
    }

    [Fact]
    public void Slack_control_characters_in_feed_text_are_escaped()
    {
        var text = Slack.Format(Content).Body;

        Assert.Contains("Quake &lt;b&gt;in&lt;/b&gt; Japan &amp; more", text);
        Assert.DoesNotContain("<b>", text);
    }

    [Fact]
    public void A_simulated_event_is_marked_SIMULATED_in_slack_and_email()
    {
        var simulated = Content with { IsSimulated = true };

        Assert.Contains("SIMULATED", Slack.Format(simulated).Body);
        var email = Email.Format(simulated);
        Assert.StartsWith("[SIMULATED] ", email.Subject);
        Assert.Contains("SIMULATED", email.Body);
    }

    [Fact]
    public void A_link_that_is_not_http_is_not_put_into_a_message()
    {
        var unsafeContent = Content with { Link = "javascript:alert(1)" };

        Assert.DoesNotContain("javascript:", Slack.Format(unsafeContent).Body);
        Assert.DoesNotContain("javascript:", Email.Format(unsafeContent).Body);
    }

    // ---- Email: format -----------------------------------------------------------------------------------------

    [Fact]
    public void Email_has_title_key_value_source_link_why_alert_link_and_disclaimer()
    {
        var message = Email.Format(Content);

        Assert.Equal("Natural disaster: Quake <b>in</b> Japan & more", message.Subject);
        Assert.Contains("Magnitude", message.Body);
        Assert.Contains("M 6.1 – Japan", message.Body);
        Assert.Contains("USGS earthquakes", message.Body);
        Assert.Contains("href=\"https://example.test/quake?a=1&amp;b=2\"", message.Body);
        Assert.Contains("Alert &quot;Japan&quot;: magnitude at least 5.0.", message.Body);
        Assert.Contains("href=\"http://localhost:8080/alerts/7\"", message.Body);
        Assert.Contains("Not an official warning.", message.Body);
    }

    [Fact]
    public void Email_html_encodes_feed_text()
    {
        var body = Email.Format(Content).Body;

        Assert.Contains("Quake &lt;b&gt;in&lt;/b&gt; Japan &amp; more", body);
        Assert.DoesNotContain("<b>in</b>", body);
    }

    [Fact]
    public void A_line_break_in_a_title_does_not_reach_the_email_subject()
    {
        var subject = Email.Format(Content with { Title = "Line one\r\nBcc: someone@example.test" }).Subject!;

        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
    }

    // ---- Slack: status policy (D13) ----------------------------------------------------------------------------

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    public void Slack_2xx_is_sent(int status) =>
        Assert.Equal(SendOutcome.Sent, SlackChannel.Classify(new HttpResponseMessage((HttpStatusCode)status)).Outcome);

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(429)]
    public void Slack_429_and_5xx_are_retried(int status) =>
        Assert.Equal(SendOutcome.Retryable,
            SlackChannel.Classify(new HttpResponseMessage((HttpStatusCode)status)).Outcome);

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(301)]
    public void Slack_other_statuses_are_final(int status) =>
        Assert.Equal(SendOutcome.Permanent,
            SlackChannel.Classify(new HttpResponseMessage((HttpStatusCode)status)).Outcome);

    [Fact]
    public void Slack_retry_after_seconds_are_passed_on()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), SlackChannel.Classify(response).RetryAfter);
    }

    // ---- Slack: send -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Slack_posts_the_text_as_json_to_the_webhook()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var channel = new SlackChannel(new SlackOptions(), new HttpClient(new StubHandler(request =>
        {
            seen = request;
            body = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        var result = await channel.SendAsync($$"""{"webhookUrl":"{{Hook}}"}""", new FormattedMessage(null, "hello"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SendOutcome.Sent, result.Outcome);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal(Hook, seen.RequestUri!.ToString());
        Assert.Equal("""{"text":"hello"}""", body);
    }

    [Fact]
    public async Task A_slack_error_text_never_contains_the_webhook_url()
    {
        var channel = new SlackChannel(new SlackOptions(), new HttpClient(new StubHandler(_ =>
            throw new HttpRequestException($"could not reach {Hook}"))));

        var result = await channel.SendAsync($$"""{"webhookUrl":"{{Hook}}"}""", new FormattedMessage(null, "hi"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SendOutcome.Retryable, result.Outcome);
        Assert.DoesNotContain("SECRET", result.Error);
        Assert.DoesNotContain("hooks.slack.com", result.Error);
    }

    [Fact]
    public async Task A_stored_config_without_a_valid_url_is_a_final_failure_and_nothing_is_sent()
    {
        var called = false;
        var channel = new SlackChannel(new SlackOptions(), new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        foreach (var config in new[] { "{}", """{"webhookUrl":"https://evil.example/x"}""", "not json" })
        {
            var result = await channel.SendAsync(config, new FormattedMessage(null, "hi"),
                TestContext.Current.CancellationToken);
            Assert.Equal(SendOutcome.Permanent, result.Outcome);
        }

        Assert.False(called);
    }

    [Fact]
    public async Task A_cancelled_slack_send_throws_instead_of_reporting_an_outcome()
    {
        var channel = new SlackChannel(new SlackOptions(), new HttpClient(new StubHandler(_ =>
            throw new TaskCanceledException())));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.SendAsync(
            $$"""{"webhookUrl":"{{Hook}}"}""", new FormattedMessage(null, "hi"), cts.Token));
    }

    // ---- Email: status policy ----------------------------------------------------------------------------------

    [Fact]
    public void Smtp_4xx_is_retried_and_5xx_is_final()
    {
        var transient = EmailChannel.Classify(
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "busy"));
        var permanent = EmailChannel.Classify(
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "no such user"));

        Assert.Equal(SendOutcome.Retryable, transient.Outcome);
        Assert.Contains("450", transient.Error);
        Assert.Equal(SendOutcome.Permanent, permanent.Outcome);
        Assert.Contains("550", permanent.Error);
    }

    [Fact]
    public void Smtp_connection_problems_and_timeouts_are_retried()
    {
        Assert.Equal(SendOutcome.Retryable, EmailChannel.Classify(new SocketException()).Outcome);
        Assert.Equal(SendOutcome.Retryable, EmailChannel.Classify(new IOException("reset")).Outcome);
        Assert.Equal(SendOutcome.Retryable, EmailChannel.Classify(new TimeoutException()).Outcome);
    }

    [Fact]
    public void Smtp_authentication_failure_is_final_and_names_no_credential()
    {
        var result = EmailChannel.Classify(new MailKit.Security.AuthenticationException("bad password hunter2"));

        Assert.Equal(SendOutcome.Permanent, result.Outcome);
        Assert.DoesNotContain("hunter2", result.Error);
    }

    [Fact]
    public async Task Email_to_a_server_that_is_not_there_is_retried()
    {
        var port = FreePort();
        var channel = new EmailChannel(new EmailOptions { Host = "127.0.0.1", Port = port });

        var result = await channel.SendAsync("""{"address":"someone@example.test"}""",
            new FormattedMessage("subject", "<p>hi</p>"), TestContext.Current.CancellationToken);

        Assert.Equal(SendOutcome.Retryable, result.Outcome);
    }

    [Fact]
    public async Task A_stored_email_config_without_a_valid_address_is_a_final_failure()
    {
        var channel = new EmailChannel(new EmailOptions());

        var result = await channel.SendAsync("""{"address":"not an address"}""", new FormattedMessage("s", "b"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SendOutcome.Permanent, result.Outcome);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
