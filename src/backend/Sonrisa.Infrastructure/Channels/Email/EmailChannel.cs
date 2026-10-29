using System.Net.Mail;
using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Channels.Email;

/// <summary>
/// Email (spec §2.6, T65, D13). Config: one address, by default the account email, copied when the destination is
/// created. The address is the owner's own data, so it is not masked. Sending is in EmailChannel.Send.cs (MailKit).
/// </summary>
public sealed partial class EmailChannel(EmailOptions options) : INotificationChannel
{
    public const string ChannelKey = "email";

    /// <summary>RFC 5321 limit of a forward path.</summary>
    public const int MaxAddressLength = 254;

    public string Key => ChannelKey;
    public string DisplayName => "Email";

    public DataValidationResult ValidateConfig(JsonElement config, ChannelContext context)
    {
        var errors = new FieldErrors();
        var input = errors.Read<EmailConfig>(config, "config");
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        var address = string.IsNullOrWhiteSpace(input.Address) ? context.AccountEmail : input.Address.Trim();
        if (!IsAddress(address))
        {
            errors.Add("address", "Enter a valid email address.");
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        return DataValidationResult.Valid(Serialize(new EmailConfig(address)));
    }

    public string MaskConfig(string configJson) => configJson;

    public string DefaultConfig(ChannelContext context) => Serialize(new EmailConfig(context.AccountEmail));

    public static EmailConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<EmailConfig>(json, JsonText.Options)
        ?? throw new JsonException("Email config is null.");

    /// <summary>
    /// A single plain address: <see cref="MailAddress"/> accepts it and gives back exactly the input, so display-name
    /// forms (<c>"Bob" &lt;b@x&gt;</c>) and lists are rejected.
    /// </summary>
    private static bool IsAddress(string value) =>
        value.Length is > 0 and <= MaxAddressLength
        && MailAddress.TryCreate(value, out var parsed)
        && parsed.Address == value
        && parsed.Host.Contains('.');

    private static string Serialize(EmailConfig config) => JsonSerializer.Serialize(config, JsonText.Options);
}

public sealed record EmailConfig(string? Address);
