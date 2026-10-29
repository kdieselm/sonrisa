namespace Sonrisa.Application;

/// <summary>Shared paging limits: no list endpoint returns a whole table.</summary>
public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Clamps <paramref name="pageSize"/> to 1..<see cref="MaxPageSize"/> and <paramref name="page"/> (1-based) so the
    /// offset <c>(page - 1) * pageSize</c> fits in <c>int</c>. A page past the end is simply empty.
    /// </summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        return (page, pageSize);
    }

    public static int Offset(int page, int pageSize) => (page - 1) * pageSize;
}
