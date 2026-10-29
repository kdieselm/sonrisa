namespace Sonrisa.Tests.Unit;

/// <summary>Recorded real responses under <c>Fixtures/</c> (fetched 2026-10-29, see process/log.md).</summary>
internal static class Fixture
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
