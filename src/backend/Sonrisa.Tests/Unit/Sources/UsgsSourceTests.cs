using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Infrastructure.Sources.Usgs;

namespace Sonrisa.Tests.Unit.Sources;

/// <summary>AC9 (USGS part): the adapter parses a recorded real response of the 4.5_day feed.</summary>
public sealed class UsgsSourceTests
{
    [Fact]
    public void Parses_every_feature_of_the_recorded_feed()
    {
        var items = UsgsSource.Parse(Fixture.Read("usgs-4.5_day.geojson"));

        Assert.Equal(22, items.Count); // metadata.count of the recording
        Assert.Equal(items.Count, items.Select(i => i.ExternalId).Distinct().Count());
        Assert.All(items, i =>
        {
            var data = DisasterCategory.Deserialize(i.DataJson);
            Assert.True(data.Magnitude >= 4.5, $"{i.ExternalId}: magnitude {data.Magnitude}");
            Assert.False(string.IsNullOrWhiteSpace(data.Place));
            Assert.StartsWith("https://earthquake.usgs.gov/", i.Url);
            Assert.NotNull(i.PublishedAt);
        });
    }

    [Fact]
    public void Maps_the_fields_of_one_feature()
    {
        var item = UsgsSource.Parse(Fixture.Read("usgs-4.5_day.geojson")).Single(i => i.ExternalId == "us7000ttyw");

        Assert.Equal("M 4.7 - 6 km SE of Pitaloza Arriba, Panama", item.Title);
        Assert.Equal("https://earthquake.usgs.gov/earthquakes/eventpage/us7000ttyw", item.Url);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793281377953), item.PublishedAt);
        Assert.Null(item.Summary);

        // GeoJSON coordinates are [longitude, latitude, depth].
        Assert.Equal(new DisasterData(4.7, "6 km SE of Pitaloza Arriba, Panama", 7.5923, -80.6309, 10),
            DisasterCategory.Deserialize(item.DataJson));
    }

    [Fact]
    public void Skips_a_feature_without_id_or_magnitude_and_falls_back_to_the_title_for_place()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","id":"a","properties":{"mag":null,"title":"no magnitude","time":1}},
              {"type":"Feature","properties":{"mag":5.0,"title":"no id","time":1}},
              {"type":"Feature","id":"c","properties":{"mag":5.1,"place":null,"title":"M 5.1 - Somewhere","time":1},
               "geometry":null}
            ]}
            """;

        var item = Assert.Single(UsgsSource.Parse(json));
        Assert.Equal("c", item.ExternalId);
        var data = DisasterCategory.Deserialize(item.DataJson);
        Assert.Equal("M 5.1 - Somewhere", data.Place);
        Assert.Null(data.Latitude);
        Assert.Null(item.Url);
    }

    [Fact]
    public void Other_ids_of_the_same_quake_become_alternate_ids()
    {
        const string json = """
            {"features":[{"id":"pt26123456","properties":{"mag":5.0,"title":"M 5.0","ids":",us7000abcd,pt26123456,"}}]}
            """;

        var item = Assert.Single(UsgsSource.Parse(json));
        Assert.Equal("pt26123456", item.ExternalId);
        Assert.Equal(["us7000abcd"], item.AlternateIds);
    }

    [Fact]
    public void Malformed_features_are_skipped_and_the_rest_of_the_feed_is_kept()
    {
        const string json = """
            {"features":[
              null,
              42,
              {"id":"far-future","properties":{"mag":5.0,"title":"bad time","time":9000000000000000}},
              {"id":"huge-mag","properties":{"mag":1e400,"title":"bad magnitude"}},
              {"id":"bad-geometry","properties":{"mag":5.2,"title":"M 5.2"},"geometry":{"coordinates":"oops"}},
              {"id":"ok","properties":{"mag":5.1,"title":"M 5.1","time":1793281377953}}
            ]}
            """;

        var items = UsgsSource.Parse(json);

        Assert.Equal(["far-future", "bad-geometry", "ok"], items.Select(i => i.ExternalId));
        Assert.Null(items[0].PublishedAt); // out-of-range time → first-seen time is used
    }

    [Fact]
    public void A_response_without_features_is_an_error()
    {
        Assert.Throws<FormatException>(() => UsgsSource.Parse("""{"type":"FeatureCollection"}"""));
    }
}
