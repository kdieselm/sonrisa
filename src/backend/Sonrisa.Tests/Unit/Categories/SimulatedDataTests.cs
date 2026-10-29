using System.Text.Json;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Categories.News;

namespace Sonrisa.Tests.Unit.Categories;

/// <summary>Each category validates the fields of the "simulate event" form and writes its own data shape.</summary>
public sealed class SimulatedDataTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Disaster_accepts_valid_fields_and_writes_its_data_shape()
    {
        var result = new DisasterCategory().ValidateSimulatedData(
            Json("""{"magnitude":6.2,"place":"  Off the coast of Chile ","latitude":-33.4,"longitude":-71.6}"""));

        Assert.True(result.IsValid);
        Assert.Equal(new DisasterData(6.2, "Off the coast of Chile", -33.4, -71.6, null),
            DisasterCategory.Deserialize(result.DataJson!));
    }

    [Theory]
    [InlineData("""{"place":"x"}""", "magnitude")]
    [InlineData("""{"magnitude":10.1,"place":"x"}""", "magnitude")]
    [InlineData("""{"magnitude":-0.1,"place":"x"}""", "magnitude")]
    [InlineData("""{"magnitude":5}""", "place")]
    [InlineData("""{"magnitude":5,"place":"   "}""", "place")]
    [InlineData("""{"magnitude":5,"place":"x","latitude":10}""", "latitude")]
    [InlineData("""{"magnitude":5,"place":"x","latitude":91,"longitude":0}""", "latitude")]
    [InlineData("""{"magnitude":5,"place":"x","latitude":0,"longitude":181}""", "longitude")]
    [InlineData("""{"magnitude":"big","place":"x"}""", "data")]
    [InlineData("""[1,2]""", "data")]
    public void Disaster_rejects_invalid_fields(string json, string field)
    {
        var result = new DisasterCategory().ValidateSimulatedData(Json(json));

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public void Disaster_accepts_both_magnitude_boundaries()
    {
        Assert.True(new DisasterCategory().ValidateSimulatedData(Json("""{"magnitude":0,"place":"x"}""")).IsValid);
        Assert.True(new DisasterCategory().ValidateSimulatedData(Json("""{"magnitude":10,"place":"x"}""")).IsValid);
    }

    [Fact]
    public void News_defaults_the_feed_name()
    {
        var result = new NewsCategory().ValidateSimulatedData(Json("{}"));

        Assert.True(result.IsValid);
        Assert.Equal(NewsCategory.SimulatedFeedName, NewsCategory.Deserialize(result.DataJson!).FeedName);
    }

    [Fact]
    public void News_rejects_a_too_long_feed_name_and_a_missing_object()
    {
        Assert.Contains("feedName", new NewsCategory()
            .ValidateSimulatedData(Json($$"""{"feedName":"{{new string('x', 101)}}"}""")).Errors.Keys);
        Assert.Contains("data", new NewsCategory().ValidateSimulatedData(default).Errors.Keys);
    }
}
