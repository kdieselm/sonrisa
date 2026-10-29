namespace Sonrisa.Domain.Categories;

/// <summary>
/// The channel-neutral content of one notification (spec §2.6, ADR-2). A category produces it
/// (<see cref="IEventCategory.Describe"/>); a channel only formats it (HTML email, plain-text Slack). No channel knows
/// a category and no category knows a channel (AC11, AC19).
/// </summary>
/// <param name="Title">The event headline.</param>
/// <param name="CategoryLabel">Display name of the category.</param>
/// <param name="KeyLabel">What <paramref name="KeyValue"/> is, for example <c>Magnitude</c>.</param>
/// <param name="KeyValue">The key value (magnitude, matched keywords, later the % move).</param>
/// <param name="SourceName">Where the event came from.</param>
/// <param name="Link">The source link of the event, if it has one.</param>
/// <param name="IsSimulated">True for an event from the admin's simulate form: shown as "SIMULATED" (AC9).</param>
/// <param name="WhyMatched">Alert name and rule in words.</param>
/// <param name="AlertPath">Path of the alert page inside the app, for example <c>/alerts/12</c>. The sender makes it absolute.</param>
/// <param name="Disclaimer">One line per category, or null.</param>
public sealed record NotificationContent(
    string Title,
    string CategoryLabel,
    string KeyLabel,
    string KeyValue,
    string SourceName,
    string? Link,
    bool IsSimulated,
    string WhyMatched,
    string AlertPath,
    string? Disclaimer)
{
    /// <summary>Set by the delivery processor from configuration; null in tests that do not need a link.</summary>
    public string? AlertUrl { get; init; }
}
