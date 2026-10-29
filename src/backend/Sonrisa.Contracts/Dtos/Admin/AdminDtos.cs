using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Destinations;

namespace Sonrisa.Contracts.Dtos.Admin;

/// <summary>A user as shown in the admin user list (spec §2.9).</summary>
public sealed record AdminUserDto(string Id, string Email, IReadOnlyList<string> Roles);

/// <summary>
/// One user with their alerts and destinations (spec §2.9), read-only. Destination configs are masked (AC18). Both
/// lists are complete: alerts and destinations per user are capped.
/// </summary>
public sealed record AdminUserDetailDto(AdminUserDto User, IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<DestinationDto> Destinations);

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
