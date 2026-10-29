namespace Sonrisa.Infrastructure.Configuration;

/// <summary>Section <c>Channels:Email</c> (spec §2.6): the SMTP server. Locally this is Mailpit.</summary>
public sealed class EmailOptions
{
    public const string Section = "Channels:Email";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "alerts@example.local";
    public string FromName { get; set; } = "Sonrisa alerts";

    /// <summary>STARTTLS when the server offers it. Off for Mailpit, which has no TLS by default.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Optional SMTP login; both empty = no authentication.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }
}
