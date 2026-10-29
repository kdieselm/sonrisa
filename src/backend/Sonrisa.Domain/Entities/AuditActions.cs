namespace Sonrisa.Domain.Entities;

/// <summary>Audit action names (spec §2.10). S3b adds the alert and destination actions.</summary>
public static class AuditActions
{
    public const string Register = "auth.register";
    public const string Login = "auth.login";
    public const string Logout = "auth.logout";
    public const string SimulateEvent = "admin.simulate_event";
}

/// <summary>Audit target type names.</summary>
public static class AuditTargets
{
    public const string User = "user";
    public const string Event = "event";
}
