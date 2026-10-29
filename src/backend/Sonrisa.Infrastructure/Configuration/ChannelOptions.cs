namespace Sonrisa.Infrastructure.Configuration;

/// <summary>Section <c>Channels:Slack</c> (spec §2.6).</summary>
public sealed class SlackOptions
{
    public const string Section = "Channels:Slack";

    /// <summary>
    /// Base URL of the local Slack mock receiver as the API itself reaches it (Compose: the service name). A new Slack
    /// destination defaults to <c>{MockBaseUrl}/hooks/{name}</c>, and URLs under it pass validation.
    /// </summary>
    public string MockBaseUrl { get; set; } = "http://localhost:8080/mock/slack";
}
