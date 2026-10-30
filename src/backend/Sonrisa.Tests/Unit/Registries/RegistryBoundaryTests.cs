using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Api.Configuration;
using Sonrisa.Application.Registries;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Infrastructure.Channels.Email;

namespace Sonrisa.Tests.Unit.Registries;

/// <summary>
/// AC11 as a test (spec §2.11, T59, T85): no code outside the registries and the type's own definition branches on a
/// channel, category or source key. Two forms are caught (S6 review H1, M1, M2):
/// <list type="bullet">
/// <item>a key as a literal: the key set is what the app really registers (registries built from
/// <c>appsettings.json</c>, so config keys such as the RSS key count too), plus every key constant found by
/// reflection;</item>
/// <item>a key through its constant: <c>==</c>, <c>!=</c>, <c>case</c>, <c>is</c>, <c>.Equals(</c> or a switch arm
/// with <c>SomeType.CategoryKey</c>, where <c>SomeType</c> is a type that defines a key constant.</item>
/// </list>
/// A type that follows the guides is covered without editing this test. The README <c>grep</c> covers the first form
/// by hand.
/// </summary>
public sealed partial class RegistryBoundaryTests
{
    private static readonly string[] KeyFieldNames = ["CategoryKey", "ChannelKey", "SourceKey", "SimulatedKey"];

    private static readonly string[] SkippedFolders =
        ["obj", "bin", "Migrations", "Sonrisa.Tests", "node_modules", "dist", ".angular"];

    private static string SrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sonrisa.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.Parent?.FullName ?? throw new InvalidOperationException("src/ folder not found from the test output.");
    }

    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(DisasterCategory).Assembly, // Domain
        typeof(EmailChannel).Assembly, // Infrastructure
        typeof(SourceRegistry).Assembly, // Application
    ];

    /// <summary>Every key constant (<c>const string CategoryKey</c> etc.) in the production assemblies.</summary>
    private static IReadOnlyList<FieldInfo> KeyConstants() =>
        ProductionAssemblies
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)
                        && KeyFieldNames.Contains(f.Name))
            .ToList();

    /// <summary>
    /// The keys the app registers, from the real registrations and <c>appsettings.json</c>. Built twice, with and
    /// without a Finnhub key, because stocks come from Finnhub or from the mock.
    /// </summary>
    private static IReadOnlySet<string> RegisteredKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { SourceRegistry.SimulatedKey };
        foreach (var finnhubKey in new[] { null, "test-key" })
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(SrcRoot(), "backend", "Sonrisa.Api", "appsettings.json"))
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sources:Market:Finnhub:ApiKey"] = finnhubKey,
                })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddLogging();
            services.AddEvents(configuration);
            services.AddChannels(configuration);
            using var provider = services.BuildServiceProvider();
            keys.UnionWith(provider.GetRequiredService<CategoryRegistry>().All.Select(c => c.Key));
            keys.UnionWith(provider.GetRequiredService<ChannelRegistry>().All.Select(c => c.Key));
            keys.UnionWith(provider.GetRequiredService<SourceRegistry>().All.Select(s => s.Key));
        }

        return keys;
    }

    private static IReadOnlyList<string> Keys() =>
        RegisteredKeys().Union(KeyConstants().Select(f => (string)f.GetRawConstantValue()!)).ToList();

    private static IEnumerable<string> Files(string root, params string[] extensions) =>
        extensions.SelectMany(e => Directory.EnumerateFiles(root, e, SearchOption.AllDirectories))
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => SkippedFolders.Contains(part)));

    /// <summary>The only line allowed to hold a key literal: the type's own key constant.</summary>
    [GeneratedRegex("""\bconst\s+string\s+(CategoryKey|ChannelKey|SourceKey|SimulatedKey)\s*=\s*"[^"]*"\s*;""")]
    private static partial Regex KeyDefinition();

    /// <summary>A branch on a key constant of the given types: comparison, case, pattern, Equals or switch arm.</summary>
    private static Regex BranchOnConstant(IEnumerable<string> typeNames)
    {
        var constant = $@"\b(?:{string.Join('|', typeNames.Select(Regex.Escape))})\.(?:{string.Join('|', KeyFieldNames)})\b";
        return new Regex($@"(?:==|!=|\bcase\b|\bis\b|\bEquals\s*\()\s*{constant}|{constant}\s*(?:==|!=|=>)");
    }

    [Fact]
    public void The_registered_keys_include_config_keys_and_every_key_constant()
    {
        var registered = RegisteredKeys();

        Assert.Contains("world-news", registered); // from appsettings.json, no constant (S6 review M2)
        Assert.Contains("finnhub", registered);
        Assert.Contains("mock-stocks", registered);
        // A key constant whose type is not registered would be dead code or a forgotten registration line.
        Assert.Subset(registered.ToHashSet(), KeyConstants().Select(f => (string)f.GetRawConstantValue()!).ToHashSet());
    }

    [Fact]
    public void Backend_code_names_a_key_literal_only_where_the_key_is_defined()
    {
        var root = Path.Combine(SrcRoot(), "backend");
        var keys = Keys();
        var hits = new List<string>();
        foreach (var file in Files(root, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var key in keys.Where(k => lines[i].Contains($"\"{k}\"", StringComparison.Ordinal)))
                {
                    if (!KeyDefinition().IsMatch(lines[i]))
                    {
                        hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: \"{key}\"");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "Keys named outside their definition:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Backend_code_never_branches_on_a_key_constant()
    {
        var root = Path.Combine(SrcRoot(), "backend");
        var pattern = BranchOnConstant(KeyConstants().Select(f => f.DeclaringType!.Name).Distinct());
        var hits = new List<string>();
        foreach (var file in Files(root, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(hits.Count == 0, "Branches on a key constant outside the registries:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Frontend_code_names_a_key_only_in_the_registries()
    {
        // Any quoted or backticked key outside a *-registry.ts file: comparisons, @case / ngSwitchCase, includes(),
        // lookup tables (S6 review M1). Allowed: the HTML attributes type, formControlName and autocomplete (an input of
        // type "email" or a form field named "email" is not the channel key), and comments.
        var root = Path.Combine(SrcRoot(), "frontend", "src");
        var keys = Keys();
        var hits = new List<string>();
        foreach (var file in Files(root, "*.ts", "*.html"))
        {
            if (file.EndsWith(".spec.ts", StringComparison.Ordinal) || file.EndsWith("-registry.ts", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*') || line.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var key in keys)
                {
                    var literal = $"""(?<!\b(?:type|formControlName|autocomplete)=)['"`]{Regex.Escape(key)}['"`]""";
                    if (Regex.IsMatch(line, literal))
                    {
                        hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: '{key}'");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "Keys named outside the frontend registries:\n" + string.Join("\n", hits));
    }
}
