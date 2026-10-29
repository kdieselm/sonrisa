namespace Sonrisa.Application;

/// <summary>
/// Result of a user-facing write: the value, field errors (400), or not found (404). "Not found" also covers another
/// user's ID, so an ID is never confirmed (spec §2.8).
/// </summary>
public sealed record ServiceResult<T>(T? Value, IReadOnlyDictionary<string, string[]>? Errors, bool IsNotFound)
    where T : class
{
    public static ServiceResult<T> Ok(T value) => new(value, null, false);

    public static ServiceResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors, false);

    public static ServiceResult<T> NotFound() => new(null, null, true);
}
