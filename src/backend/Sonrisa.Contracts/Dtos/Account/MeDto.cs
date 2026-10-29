namespace Sonrisa.Contracts.Dtos.Account;

/// <summary>The caller's account, read by the SPA to decide what to show (the bearer token is opaque).</summary>
public sealed record MeDto(string Email, IReadOnlyList<string> Roles);
