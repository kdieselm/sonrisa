using System.Text.RegularExpressions;

namespace Sonrisa.Tests.Unit.Registries;

/// <summary>
/// S6: every file path a "how to add" guide names exists in the repository (spec §2.11, AC11: "each guide lists the
/// files it adds and the one registration it changes"). Tokens with a placeholder (<c>&lt;Name&gt;</c>) are patterns
/// for new files and are skipped; the rest must resolve, from the repository root or as the tail of a path under
/// <c>src/</c>. A rename that leaves a guide behind fails here.
/// </summary>
public sealed partial class GuidePathTests
{
    private static readonly string[] Extensions = [".cs", ".ts", ".json", ".md", ".yml", ".xml", ".geojson"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("`([^`\\s]+)`")]
    private static partial Regex Backticked();

    private static IEnumerable<string> Tokens(string markdown) =>
        Backticked().Matches(markdown).Select(m => m.Groups[1].Value.Trim().TrimEnd(',', '.', ':', ';'))
            .Where(t => !t.Contains('<') && !t.Contains('*') && !t.Contains('{') && !t.Contains('(')
                        && !t.StartsWith("http", StringComparison.Ordinal)
                        && Extensions.Any(e => t.EndsWith(e, StringComparison.Ordinal)) && t.Contains('/'))
            .Distinct();

    private static bool Resolves(string root, string[] srcFiles, string token)
    {
        var clean = token.Replace('\\', '/').TrimStart('/');
        if (File.Exists(Path.Combine(root, clean)))
        {
            return true;
        }

        return srcFiles.Any(f => f.EndsWith("/" + clean, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("add-a-category.md")]
    [InlineData("add-a-channel.md")]
    [InlineData("add-a-source.md")]
    public void Every_path_a_guide_names_exists(string guide)
    {
        var root = RepoRoot();
        var srcFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("node_modules"))
            .Select(f => "/" + f)
            .ToArray();
        var text = File.ReadAllText(Path.Combine(root, "docs", "how-to", guide));

        var tokens = Tokens(text).ToList();
        var missing = tokens.Where(t => !Resolves(root, srcFiles, t)).ToList();

        Assert.NotEmpty(tokens); // the check must not pass because it found nothing
        Assert.True(missing.Count == 0, $"{guide} names files that do not exist:\n" + string.Join("\n", missing));
    }
}
