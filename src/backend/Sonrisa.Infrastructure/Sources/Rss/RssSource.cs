using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Sources.Rss;

/// <summary>
/// One RSS 2.0 news feed (spec §2.5, F7); BBC World and Guardian World are two configured instances. External ID =
/// <c>guid</c>, else <c>link</c> (spec §2.3); an item without <c>pubDate</c> gets the first-seen time. Parsed with
/// <c>XDocument</c>, no package (D24); DTDs are refused.
/// </summary>
public sealed partial class RssSource(SourceHttpClient http, RssFeedOptions options) : IEventSource
{
    public string Key => options.Key;
    public string DisplayName => options.DisplayName;
    public string CategoryKey => NewsCategory.CategoryKey;
    public TimeSpan Interval => options.Interval;

    public async Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
    {
        var xml = await http.Http.GetStringAsync(options.Url, ct);
        return Parse(xml, options.DisplayName);
    }

    /// <summary>Parses a feed; an item without a title, or without both guid and link, is skipped.</summary>
    public static IReadOnlyList<IncomingEvent> Parse(string xml, string feedName)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var document = XDocument.Load(reader);

        var channel = document.Root?.Element("channel")
                      ?? throw new FormatException("RSS response has no <rss><channel> element.");
        var data = NewsCategory.Serialize(new NewsData(feedName));

        var result = new List<IncomingEvent>();
        foreach (var item in channel.Elements("item"))
        {
            var title = Text(item, "title");
            var link = Text(item, "link");
            var externalId = Text(item, "guid") ?? link;
            if (title is null || externalId is null)
            {
                continue;
            }

            result.Add(new IncomingEvent(externalId, title, PlainText(Text(item, "description")),
                IsHttpUrl(link) ? link : null, ParseDate(Text(item, "pubDate")), data));
        }

        return result;
    }

    /// <summary>RFC 822 dates as feeds write them ("Thu, 29 Oct 2026 11:07:35 GMT"); unknown formats → null.</summary>
    public static DateTimeOffset? ParseDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out var exact))
        {
            return exact;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Descriptions may hold HTML (the Guardian's do); the summary keeps plain text only.</summary>
    public static string? PlainText(string? html)
    {
        if (html is null)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Text(XElement item, string name)
    {
        var value = item.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
