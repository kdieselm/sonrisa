using System.Reflection;
using System.Text.RegularExpressions;

namespace Sonrisa.Tests.Unit.Registries;

/// <summary>
/// S6: everything a "how to add" guide points at exists (spec §2.11, AC11: "each guide lists the files it adds and the
/// one registration it changes"). Checked per backticked token:
/// <list type="bullet">
/// <item>a file (with or without a folder): resolves from the repository root or as the tail of a path under
/// <c>src/</c>;</item>
/// <item>a folder (ends with <c>/</c>), and the fixed folder in front of a placeholder path
/// (<c>Sonrisa.Domain/Categories/&lt;Name&gt;/…</c> → <c>Sonrisa.Domain/Categories/</c>): the folder exists;</item>
/// <item>a test class (<c>…Tests</c>) or test method (<c>…Tests.Method_name</c>): exists in the test assembly.</item>
/// </list>
/// A rename that leaves a guide behind fails here (S6 review L5 added folders, bare file names and test names).
/// </summary>
public sealed partial class GuidePathTests
{
    private static readonly string[] Extensions = [".cs", ".ts", ".json", ".md", ".yml", ".xml", ".geojson"];

    public static TheoryData<string> Guides => new() { "add-a-category.md", "add-a-channel.md", "add-a-source.md" };

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

    [GeneratedRegex(@"^\w+Tests(\.\w+)?$")]
    private static partial Regex TestName();

    private static List<string> Tokens(string guide) =>
        Backticked().Matches(File.ReadAllText(Path.Combine(RepoRoot(), "docs", "how-to", guide)))
            .Select(m => m.Groups[1].Value.Trim().TrimEnd(',', '.', ':', ';'))
            .Where(t => !t.StartsWith("http", StringComparison.Ordinal))
            .Distinct()
            .ToList();

    private static bool HasPlaceholder(string t) => t.Contains('<') || t.Contains('*') || t.Contains('{') || t.Contains('(');

    /// <summary>Files and folders under the repository, as "/relative/path" with forward slashes.</summary>
    private static (string[] Files, string[] Folders) Tree()
    {
        var root = RepoRoot();
        static bool Kept(string p) => !p.Contains("/obj/") && !p.Contains("/bin/") && !p.Contains("node_modules");
        string Rel(string p) => "/" + Path.GetRelativePath(root, p).Replace('\\', '/');
        string[] tops = [Path.Combine(root, "src"), Path.Combine(root, "docs")]; // guides also name each other
        return (
            tops.SelectMany(t => Directory.EnumerateFiles(t, "*", SearchOption.AllDirectories)).Select(Rel).Where(Kept).ToArray(),
            tops.SelectMany(t => Directory.EnumerateDirectories(t, "*", SearchOption.AllDirectories)).Select(Rel).Where(Kept).ToArray());
    }

    /// <summary>
    /// From the root, or as the tail of a path; a tail may also start after a dot, because the guides shorten
    /// <c>Sonrisa.Infrastructure/…</c> to <c>Infrastructure/…</c>.
    /// </summary>
    private static bool Resolves(string[] entries, string token, Func<string, bool> existsFromRoot)
    {
        var clean = token.Replace('\\', '/').Trim('/');
        return existsFromRoot(Path.Combine(RepoRoot(), clean))
               || entries.Any(e => e.EndsWith("/" + clean, StringComparison.Ordinal)
                                   || e.EndsWith("." + clean, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Guides))]
    public void Every_file_a_guide_names_exists(string guide)
    {
        var (files, _) = Tree();
        var tokens = Tokens(guide)
            .Where(t => !HasPlaceholder(t) && Extensions.Any(e => t.EndsWith(e, StringComparison.Ordinal)))
            .ToList();

        var missing = tokens.Where(t => !Resolves(files, t, File.Exists)).ToList();

        Assert.NotEmpty(tokens); // the check must not pass because it found nothing
        Assert.True(missing.Count == 0, $"{guide} names files that do not exist:\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(Guides))]
    public void Every_folder_a_guide_names_exists_also_in_front_of_a_placeholder(string guide)
    {
        var (_, folders) = Tree();
        var tokens = Tokens(guide)
            .Select(t => HasPlaceholder(t) ? t[..(t.IndexOfAny(['<', '*', '{', '(']))] : t)
            .Where(t => t.EndsWith('/') || (t.Contains('/') && HasPlaceholder(t)))
            .Select(t => t[..(t.LastIndexOf('/') + 1)])
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();

        var missing = tokens.Where(t => !Resolves(folders, t, Directory.Exists)).ToList();

        Assert.NotEmpty(tokens);
        Assert.True(missing.Count == 0, $"{guide} names folders that do not exist:\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(Guides))]
    public void Every_test_a_guide_names_exists(string guide)
    {
        var types = typeof(GuidePathTests).Assembly.GetTypes().ToLookup(t => t.Name);
        var tokens = Tokens(guide).Where(t => TestName().IsMatch(t)).ToList();

        var missing = tokens.Where(t =>
        {
            var parts = t.Split('.');
            return !types[parts[0]].Any(type => parts.Length == 1
                                               || type.GetMethod(parts[1], BindingFlags.Public | BindingFlags.Instance) is not null);
        }).ToList();

        Assert.NotEmpty(tokens);
        Assert.True(missing.Count == 0, $"{guide} names tests that do not exist:\n" + string.Join("\n", missing));
    }
}
