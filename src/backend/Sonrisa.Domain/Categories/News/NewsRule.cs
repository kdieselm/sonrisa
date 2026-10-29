using System.Text.RegularExpressions;

namespace Sonrisa.Domain.Categories.News;

/// <summary>
/// Rule of a news alert (spec §2.2, D15): 1–10 keywords or phrases; any one of them in the headline is a match.
/// </summary>
public sealed record NewsRule(IReadOnlyList<string> Keywords)
{
    public const int MaxKeywords = 10;
    public const int MaxKeywordLength = 100;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The keywords of this rule that occur in <paramref name="headline"/>, in rule order.</summary>
    public IReadOnlyList<string> MatchedKeywords(string headline) =>
        Keywords.Where(k => Occurs(k, headline)).ToList();

    /// <summary>
    /// Whole word or phrase, case-insensitive, culture-invariant. "Not inside another word" means no letter, digit or
    /// underscore directly before or after; lookarounds are used instead of <c>\b</c> so a keyword that starts or ends
    /// with a symbol ("C++", "#AI") still works. Whitespace inside a phrase matches any run of whitespace.
    /// </summary>
    public static bool Occurs(string keyword, string headline)
    {
        var parts = keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        var pattern = $@"(?<!\w){string.Join(@"\s+", parts)}(?!\w)";
        try
        {
            return Regex.IsMatch(headline, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
