using System.Xml;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Infrastructure.Sources.Rss;

namespace Sonrisa.Tests.Unit.Sources;

/// <summary>AC9 (RSS part) and spec §2.3: recorded BBC and Guardian feeds; guid/link and pubDate rules.</summary>
public sealed class RssSourceTests
{
    [Fact]
    public void Parses_the_recorded_BBC_feed()
    {
        var items = RssSource.Parse(Fixture.Read("bbc-world.xml"), "BBC News – World");

        Assert.Equal(27, items.Count);
        var first = items[0];
        Assert.Equal("https://www.bbc.co.uk/news/articles/cmqxnl312v34o#0", first.ExternalId); // guid, not link
        Assert.Equal("Audio reveals exchanges between Spain train operators and drivers moments before deadly crash",
            first.Title);
        Assert.Equal("https://www.bbc.co.uk/news/articles/cmqxnl312v34o?at_medium=RSS&at_campaign=rss", first.Url);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 11, 7, 35, TimeSpan.Zero), first.PublishedAt);
        Assert.StartsWith("The leaked tapes", first.Summary);
        Assert.Equal("BBC News – World", NewsCategory.Deserialize(first.DataJson).FeedName);
        Assert.All(items, i => Assert.NotNull(i.PublishedAt));
    }

    [Fact]
    public void Parses_the_recorded_Guardian_feed_and_strips_html_from_the_summary()
    {
        var items = RssSource.Parse(Fixture.Read("guardian-world.xml"), "The Guardian – World");

        Assert.Equal(45, items.Count);
        var first = items[0];
        Assert.Equal("Egypt fines prominent editor Lina Attalah and shuts independent news outlet Mada Masr", first.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 14, 16, 24, TimeSpan.Zero), first.PublishedAt);
        Assert.StartsWith("Warnings of escalating clampdown", first.Summary);
        Assert.All(items, i => Assert.DoesNotContain("<p>", i.Summary ?? string.Empty));
    }

    [Fact]
    public void Uses_link_when_guid_is_missing_and_null_time_when_pubDate_is_missing()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>No guid</title><link>https://example.test/a</link></item>
              <item><title>Odd date</title><guid>g-2</guid><pubDate>sometime yesterday</pubDate></item>
              <item><title> </title><guid>g-3</guid></item>
              <item><title>No id at all</title></item>
              <item><title>Not a web link</title><guid>g-5</guid><link>javascript:alert(1)</link></item>
            </channel></rss>
            """;

        var items = RssSource.Parse(xml, "Test");

        Assert.Equal(["https://example.test/a", "g-2", "g-5"], items.Select(i => i.ExternalId));
        Assert.All(items, i => Assert.Null(i.PublishedAt)); // the ingestor uses the first-seen time
        Assert.Equal("https://example.test/a", items[0].Url);
        Assert.Null(items[2].Url);
    }

    [Theory]
    [InlineData("Thu, 29 Oct 2026 11:07:35 GMT", "2026-10-29T11:07:35+00:00")]
    [InlineData("Thu, 29 Oct 2026 11:07:35 +0100", "2026-10-29T11:07:35+01:00")]
    [InlineData("2026-10-29T11:07:35Z", "2026-10-29T11:07:35+00:00")]
    public void Parses_common_feed_dates(string value, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            RssSource.ParseDate(value));
    }

    [Fact]
    public void Refuses_a_document_with_a_DTD()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY x "boom">]>
            <rss version="2.0"><channel><item><title>&x;</title><guid>1</guid></item></channel></rss>
            """;

        Assert.Throws<XmlException>(() => RssSource.Parse(xml, "Test"));
    }

    [Fact]
    public void A_document_that_is_not_rss_is_an_error()
    {
        Assert.Throws<FormatException>(() => RssSource.Parse("<feed/>", "Test"));
    }
}
