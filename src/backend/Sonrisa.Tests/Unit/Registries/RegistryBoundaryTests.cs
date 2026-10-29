using System.Reflection;
using System.Text.RegularExpressions;
using Sonrisa.Application.Registries;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Infrastructure.Channels.Email;

namespace Sonrisa.Tests.Unit.Registries;

/// <summary>
/// AC11 as a test (spec §2.11, T59, T85): no code outside the registries and the type's own definition names a
/// channel, category or source key. Keys are found by reflection (every <c>const string</c> named
/// <c>CategoryKey</c>, <c>ChannelKey</c> or <c>SourceKey</c> in the production assemblies), so a type that follows the
/// guides is covered without editing this test. The same rule as the README <c>grep</c>.
/// </summary>
public sealed partial class RegistryBoundaryTests
{
    private static readonly string[] KeyFieldNames = ["CategoryKey", "ChannelKey", "SourceKey", "SimulatedKey"];

    private static readonly string[] SkippedFolders = ["obj", "bin", "Migrations", "Sonrisa.Tests", "node_modules", "dist", ".angular"];

    private static string SrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sonrisa.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.Parent?.FullName ?? throw new InvalidOperationException("src/ folder not found from the test output.");
    }

    /// <summary>Every key of every channel, category and source in the production assemblies.</summary>
    private static IReadOnlyList<string> Keys()
    {
        var assemblies = new[]
        {
            typeof(DisasterCategory).Assembly, // Domain
            typeof(EmailChannel).Assembly, // Infrastructure
            typeof(SourceRegistry).Assembly, // Application
        };
        return assemblies
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)
                        && KeyFieldNames.Contains(f.Name))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct()
            .ToList();
    }

    private static IEnumerable<string> Files(string root, string extension) =>
        Directory.EnumerateFiles(root, extension, SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => SkippedFolders.Contains(part)));

    [GeneratedRegex("""const\s+string\s+\w+\s*=\s*"[^"]*"\s*;""")]
    private static partial Regex ConstDefinition();

    [Fact]
    public void The_keys_are_found_by_reflection()
    {
        var keys = Keys();

        Assert.Subset(keys.ToHashSet(),
            new HashSet<string> { "disaster", "news", "market", "email", "slack", "usgs", "coingecko", "simulated" });
    }

    [Fact]
    public void Backend_code_names_a_key_only_where_the_key_is_defined()
    {
        var root = Path.Combine(SrcRoot(), "backend");
        var keys = Keys();
        var hits = new List<string>();
        foreach (var file in Files(root, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var key in keys.Where(k => line.Contains($"\"{k}\"", StringComparison.Ordinal)))
                {
                    if (!ConstDefinition().IsMatch(line))
                    {
                        hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: \"{key}\"");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "Keys named outside their definition:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Frontend_code_never_compares_with_or_switches_on_a_key()
    {
        var root = Path.Combine(SrcRoot(), "frontend", "src");
        var keys = Keys();
        var hits = new List<string>();
        foreach (var file in Files(root, "*.ts"))
        {
            if (file.EndsWith(".spec.ts", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var key in keys)
                {
                    var q = $"""['"]{Regex.Escape(key)}['"]""";
                    if (Regex.IsMatch(lines[i], $@"(===?|!==?|\bcase)\s*{q}|{q}\s*(===?|!==?)"))
                    {
                        hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: '{key}'");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "Keys compared in the frontend:\n" + string.Join("\n", hits));
    }
}
