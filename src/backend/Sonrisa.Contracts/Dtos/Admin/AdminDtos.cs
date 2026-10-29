namespace Sonrisa.Contracts.Dtos.Admin;

/// <summary>A user as shown in the admin view (spec §2.9). Alerts and destinations are added in S3b.</summary>
public sealed record AdminUserDto(string Id, string Email, IReadOnlyList<string> Roles);

/// <summary>One audit log row (spec §2.10). <c>ActorEmail</c> is resolved for display; null if unknown.</summary>
public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset At,
    string? ActorUserId,
    string? ActorEmail,
    string Action,
    string? TargetType,
    string? TargetId,
    string Outcome);
