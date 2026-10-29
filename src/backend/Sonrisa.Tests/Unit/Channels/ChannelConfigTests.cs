using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Infrastructure.Channels.Email;
using Sonrisa.Infrastructure.Channels.Slack;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Tests.Unit.Channels;

/// <summary>Spec §2.6, T69, AC18: channel config validation, defaults and masking.</summary>
public sealed class ChannelConfigTests
{
    private const string MockBase = "http://api:8080/mock/slack";
    private static readonly ChannelContext Context = new("user-1", "owner@example.test");
    private static readonly SlackChannel Slack = new(new SlackOptions { MockBaseUrl = MockBase }, new HttpClient());
    private static readonly EmailChannel Email = new(new EmailOptions());

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Email_defaults_to_the_account_email()
    {
        var result = Email.ValidateConfig(Json("{}"), Context);

        Assert.True(result.IsValid);
        Assert.Equal("owner@example.test", EmailChannel.Deserialize(result.DataJson!).Address);
        Assert.Equal("owner@example.test", EmailChannel.Deserialize(Email.DefaultConfig(Context)).Address);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("a@b")]
    [InlineData("\"Bob\" <bob@example.test>")]
    [InlineData("a@example.test, b@example.test")]
    public void Email_rejects_anything_but_one_plain_address(string address)
    {
        var result = Email.ValidateConfig(JsonSerializer.SerializeToElement(new { address }), Context);

        Assert.False(result.IsValid);
        Assert.Contains("address", result.Errors.Keys);
    }

    [Fact]
    public void Slack_defaults_to_a_fresh_mock_receiver_url()
    {
        var first = SlackChannel.Deserialize(Slack.ValidateConfig(Json("{}"), Context).DataJson!).WebhookUrl!;
        var second = SlackChannel.Deserialize(Slack.ValidateConfig(Json("{}"), Context).DataJson!).WebhookUrl!;

        Assert.StartsWith(MockBase + "/hooks/", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("https://hooks.slack.com/services/T000/B000/XXXX")]
    [InlineData("http://api:8080/mock/slack/fail")]
    [InlineData("http://api:8080/mock/slack/hooks/team")]
    public void Slack_accepts_a_real_webhook_or_a_mock_url(string url)
    {
        Assert.True(Slack.ValidateConfig(JsonSerializer.SerializeToElement(new { webhookUrl = url }), Context).IsValid);
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/T000/B000/XXXX")] // not https
    [InlineData("https://hooks.slack.com.evil.test/services/x")]
    [InlineData("https://user@hooks.slack.com/services/x")]
    [InlineData("https://example.test/hook")]
    [InlineData("http://api:8080/mock/slackish")]
    [InlineData("http://api:8080/mock/slack/../../admin")]
    [InlineData("http://api:8080/mock/slack/%2e%2e/%2e%2e/api/admin")]
    [InlineData("http://api:8080/mock/slack/hooks/x?redirect=1")]
    [InlineData("http://api:9090/mock/slack/hooks/x")]
    [InlineData("http://evil.test/mock/slack/hooks/x")]
    [InlineData("not a url")]
    public void Slack_rejects_other_urls(string url)
    {
        var result = Slack.ValidateConfig(JsonSerializer.SerializeToElement(new { webhookUrl = url }), Context);

        Assert.False(result.IsValid);
        Assert.Contains("webhookUrl", result.Errors.Keys);
    }

    [Fact]
    public void On_update_an_empty_slack_url_keeps_the_stored_one()
    {
        const string stored = """{"webhookUrl":"https://hooks.slack.com/services/T000/B000/REAL"}""";
        var update = Context with { StoredConfig = stored };

        var kept = Slack.ValidateConfig(Json("""{"webhookUrl":""}"""), update);

        Assert.Equal("https://hooks.slack.com/services/T000/B000/REAL",
            SlackChannel.Deserialize(kept.DataJson!).WebhookUrl);
    }

    [Fact]
    public void Slack_masks_all_but_the_last_four_characters()
    {
        const string url = "https://hooks.slack.com/services/T000/B000/SECRETabcd";
        var stored = Slack.ValidateConfig(JsonSerializer.SerializeToElement(new { webhookUrl = url }), Context).DataJson!;

        var masked = Slack.MaskConfig(stored);

        Assert.DoesNotContain("SECRET", masked);
        Assert.DoesNotContain("hooks.slack.com", masked);
        Assert.Equal("…abcd", SlackChannel.Deserialize(masked).WebhookUrl);
        Assert.Null(SlackChannel.Deserialize(Slack.DefaultConfig(Context)).WebhookUrl);
    }
}
