namespace Sonrisa.Domain.Entities;

/// <summary>Audit action names (spec §2.10).</summary>
public static class AuditActions
{
    public const string Register = "auth.register";
    public const string Login = "auth.login";
    public const string Logout = "auth.logout";
    public const string SimulateEvent = "admin.simulate_event";
    public const string AlertCreate = "alert.create";
    public const string AlertUpdate = "alert.update";
    public const string AlertDelete = "alert.delete";
    public const string AlertEnable = "alert.enable";
    public const string AlertDisable = "alert.disable";
    public const string DestinationCreate = "destination.create";
    public const string DestinationUpdate = "destination.update";
    public const string DestinationDelete = "destination.delete";
}

/// <summary>Audit target type names.</summary>
public static class AuditTargets
{
    public const string User = "user";
    public const string Event = "event";
    public const string Alert = "alert";
    public const string Destination = "destination";
}
