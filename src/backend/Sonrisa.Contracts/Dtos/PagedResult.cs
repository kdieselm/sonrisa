namespace Sonrisa.Contracts.Dtos;

/// <summary>One page of a list; <c>Page</c> is 1-based.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
