namespace Sonrisa.Application.Abstractions;

/// <summary>
/// Runs a unit of work in one database transaction (ADR-3): pending changes are saved and committed together, or
/// nothing is. On failure the transaction is rolled back and the pending changes are discarded.
/// </summary>
public interface ITransactionRunner
{
    /// <exception cref="DuplicateKeyException">A unique index rejected the insert.</exception>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}

/// <summary>A unique index rejected a write; the transaction was rolled back.</summary>
/// <param name="ConstraintName">The index that rejected it, if the database named it.</param>
public sealed class DuplicateKeyException(string message, Exception inner, string? constraintName = null)
    : Exception(message, inner)
{
    public string? ConstraintName { get; } = constraintName;
}
