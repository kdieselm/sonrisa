namespace Sonrisa.Domain.Categories;

/// <summary>
/// Result of <see cref="IEventCategory.Evaluate"/>: whether the event matches, and the alert's new state JSON (null =
/// the category keeps no state or nothing changed).
/// </summary>
public sealed record MatchOutcome(bool Matched, string? NewStateJson = null);
