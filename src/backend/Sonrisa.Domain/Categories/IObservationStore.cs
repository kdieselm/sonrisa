namespace Sonrisa.Domain.Categories;

/// <summary>
/// The last observed value per asset (spec §2.4, ADR-5): in memory, no price history (T22). A market source records
/// each poll here; a category reads it to give a new alert its start state. After a restart it is empty until the
/// first poll (spec §7).
/// </summary>
public interface IObservationStore
{
    /// <summary>The last observed change in percent of <paramref name="asset"/>, or null if none yet.</summary>
    double? Latest(string asset);

    /// <summary>Stores <paramref name="changePercent"/> and returns the value it replaced (null on the first poll).</summary>
    double? Update(string asset, double changePercent);
}
