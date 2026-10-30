using System.Globalization;
using System.Text.Json;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Domain.Categories.Market;

/// <summary>Market movements: crypto and stocks, as the provider's percentage change (spec §2.2, §2.4).</summary>
public sealed class MarketCategory(MarketAssetCatalog assets) : IEventCategory
{
    public const string CategoryKey = "market";
    public const string SimulatedBaseline = "simulated move";
    public const string Disclaimer = "Not financial advice. Data may be delayed.";

    private static readonly MarketSide Armed = new(true, null);

    public string Key => CategoryKey;
    public string DisplayName => "Market movement";

    /// <summary>Market events are observations at poll time, never old (spec §2.5).</summary>
    public TimeSpan? MaxAge => null;

    public bool SupportsBackfill => false;

    public string? BackfillUnavailableReason =>
        "Market alerts fire on new crossings only; there is no price history to look back on.";

    public static string Serialize(MarketData data) => JsonSerializer.Serialize(data, JsonText.Options);

    public static MarketData Deserialize(string json) =>
        JsonSerializer.Deserialize<MarketData>(json, JsonText.Options)
        ?? throw new JsonException("Market data is null.");

    public static MarketRule DeserializeRule(string json) =>
        JsonSerializer.Deserialize<MarketRule>(json, JsonText.Options)
        ?? throw new JsonException("Market rule is null.");

    public static MarketState DeserializeState(string json) =>
        JsonSerializer.Deserialize<MarketState>(json, JsonText.Options)
        ?? throw new JsonException("Market state is null.");

    private static string Serialize(MarketState state) => JsonSerializer.Serialize(state, JsonText.Options);

    public DataValidationResult ValidateSimulatedData(JsonElement data)
    {
        var errors = new FieldErrors();
        var input = errors.Read<SimulatedMarketInput>(data);
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        var asset = assets.Find(input.Asset);
        if (asset is null)
        {
            errors.Add("asset", "Choose one of the configured assets.");
        }

        CheckPercent(input.PreviousChangePercent, "previousChangePercent", "Previous change", errors);
        CheckPercent(input.CurrentChangePercent, "currentChangePercent", "Current change", errors);

        return errors.Any
            ? DataValidationResult.Invalid(errors.ToDictionary())
            : DataValidationResult.Valid(Serialize(new MarketData(asset!.Key, asset.DisplayName,
                input.PreviousChangePercent!.Value, input.CurrentChangePercent!.Value, SimulatedBaseline)));
    }

    public DataValidationResult ValidateRule(JsonElement rule)
    {
        var errors = new FieldErrors();
        var input = errors.Read<RuleInput>(rule, "rule");
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        var asset = assets.Find(input.Asset);
        if (asset is null)
        {
            errors.Add("asset", "Choose one of the configured assets.");
        }

        var direction = input.Direction?.Trim().ToLowerInvariant();
        if (direction is not (MarketRule.Up or MarketRule.Down or MarketRule.Either))
        {
            errors.Add("direction", "Direction must be up, down or either.");
        }

        if (input.ThresholdPercent is not { } threshold || !double.IsFinite(threshold)
            || threshold is < MarketRule.MinThreshold or > MarketRule.MaxPercent
            || !MarketLogic.IsMultipleOfGrid(threshold))
        {
            errors.Add("thresholdPercent",
                $"Threshold is required: {MarketRule.MinThreshold} to {MarketRule.MaxPercent} percent, in steps of 0.5.");
        }

        if (input.StepPercent is { } step && (!double.IsFinite(step)
                                              || step is < MarketLogic.Grid or > MarketRule.MaxPercent
                                              || !MarketLogic.IsMultipleOfGrid(step)))
        {
            errors.Add("stepPercent", $"Step is optional: 0.5 to {MarketRule.MaxPercent} percent, in steps of 0.5.");
        }

        return errors.Any
            ? DataValidationResult.Invalid(errors.ToDictionary())
            : DataValidationResult.Valid(JsonSerializer.Serialize(
                new MarketRule(asset!.Key, direction!, input.ThresholdPercent!.Value, input.StepPercent),
                JsonText.Options));
    }

    /// <summary>
    /// Stateless check: the event is for the asset and its change is past the threshold in the rule's direction. The
    /// ingestor uses <see cref="Evaluate"/>, which adds re-arm and steps.
    /// </summary>
    public bool Matches(Event item, string ruleJson)
    {
        var data = Deserialize(item.Data);
        var rule = DeserializeRule(ruleJson);
        return IsAsset(data, rule)
               && ((Watches(rule, up: true) && data.CurrentChangePercent >= rule.ThresholdPercent)
                   || (Watches(rule, up: false) && -data.CurrentChangePercent >= rule.ThresholdPercent));
    }

    /// <summary>Fire / escalate / re-arm per direction (spec §2.4, AC8).</summary>
    public MatchOutcome Evaluate(Event item, string ruleJson, string? stateJson)
    {
        var data = Deserialize(item.Data);
        var rule = DeserializeRule(ruleJson);
        if (!IsAsset(data, rule))
        {
            return new MatchOutcome(false);
        }

        var state = stateJson is null ? new MarketState(null, null) : DeserializeState(stateJson);
        var stored = state;
        if (state.StartPending)
        {
            state = StartState(rule, data.PreviousChangePercent);
        }

        var fired = false;
        var up = state.Up;
        var down = state.Down;
        if (Watches(rule, up: true))
        {
            var (f, side) = MarketLogic.Step(state.Up ?? Armed, data.CurrentChangePercent, rule.ThresholdPercent,
                rule.StepPercent);
            fired |= f;
            up = side;
        }

        if (Watches(rule, up: false))
        {
            var (f, side) = MarketLogic.Step(state.Down ?? Armed, -data.CurrentChangePercent, rule.ThresholdPercent,
                rule.StepPercent);
            fired |= f;
            down = side;
        }

        var next = new MarketState(up, down);
        return new MatchOutcome(fired, next == stored ? null : Serialize(next));
    }

    /// <summary>
    /// Start state from the latest observed value (spec §2.4). With no observation yet (after a restart, or while the
    /// provider fails) the start is left pending instead of armed, so a move already past the threshold cannot notify.
    /// </summary>
    public string? InitialState(string ruleJson, IObservationStore observations)
    {
        var rule = DeserializeRule(ruleJson);
        return observations.Latest(rule.Asset) is { } latest
            ? Serialize(StartState(rule, latest))
            : Serialize(new MarketState(null, null, StartPending: true));
    }

    private static MarketState StartState(MarketRule rule, double latest) => new(
        Watches(rule, up: true) ? MarketLogic.Start(latest, rule.ThresholdPercent, rule.StepPercent) : null,
        Watches(rule, up: false) ? MarketLogic.Start(-latest, rule.ThresholdPercent, rule.StepPercent) : null);

    public NotificationContent Describe(Event item, Alert alert)
    {
        var data = Deserialize(item.Data);
        var rule = DeserializeRule(alert.Rule);
        var direction = rule.Direction switch
        {
            MarketRule.Up => "rises",
            MarketRule.Down => "falls",
            _ => "moves",
        };
        var why = string.Create(CultureInfo.InvariantCulture,
            $"Alert \"{alert.Name}\": {data.AssetName} {direction} by at least {rule.ThresholdPercent:0.0#}%");
        if (rule.StepPercent is { } step)
        {
            why += string.Create(CultureInfo.InvariantCulture, $", and again for every further {step:0.0#}%");
        }

        var key = $"{Signed(data.PreviousChangePercent)} → {Signed(data.CurrentChangePercent)} ({data.Baseline})";
        return new NotificationContent(item.Title, DisplayName, "Change", key, item.SourceKey, item.Url,
            item.IsSimulated, why + ".", $"/alerts/{alert.Id}", Disclaimer);
    }

    /// <summary>Title of a market event, for sources and the simulate path alike.</summary>
    public static string TitleOf(MarketData data) =>
        $"{data.AssetName}: {Signed(data.CurrentChangePercent)} (was {Signed(data.PreviousChangePercent)})";

    public static string Signed(double percent) =>
        percent.ToString("+0.0#;-0.0#;0.0", CultureInfo.InvariantCulture) + "%";

    private static bool IsAsset(MarketData data, MarketRule rule) =>
        string.Equals(data.Asset, rule.Asset, StringComparison.OrdinalIgnoreCase);

    private static bool Watches(MarketRule rule, bool up) =>
        rule.Direction == MarketRule.Either || rule.Direction == (up ? MarketRule.Up : MarketRule.Down);

    private static void CheckPercent(double? value, string field, string label, FieldErrors errors)
    {
        if (value is not { } v || !double.IsFinite(v) || v is < -100 or > 1000)
        {
            errors.Add(field, $"{label} is required, between -100 and 1000 percent.");
        }
    }

    private sealed record RuleInput(string? Asset, string? Direction, double? ThresholdPercent, double? StepPercent);

    private sealed record SimulatedMarketInput(string? Asset, double? PreviousChangePercent,
        double? CurrentChangePercent);
}
