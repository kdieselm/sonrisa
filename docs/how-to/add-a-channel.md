# How to add a notification channel

A **notification channel** is a way to deliver a notification: email, Slack, later for example a generic webhook or SMS (spec §2.6, §2.11). The brief asks for a system "flexible enough that we can add more channels later". A channel owns the shape of a destination's config: it validates it, fills in defaults, and decides what of it may leave the API. It also turns the channel-neutral `NotificationContent` into its own message and sends it. Core code never names a channel: it finds one through the registry by key (ADR-2, AC11).

- **Status:** S6. Config, validation, defaults, masking, the destination form, formatting and sending (`Format`, `SendAsync`) are complete. Dry-run in S6 (`process/log.md`): a throwaway `console` channel built from this guide alone took **2 new files** (one backend class, one frontend file), **2 registration lines** (one per side) and **1 test update** (the catalog test that lists the channels, see the checklist); backend and frontend built; of the 312 tests, 310 passed and 2 failed: the catalog test named above and a flaky delivery test unrelated to the channel (fixed in S6, see the log).
- **Worked example:** `slack` (the config holds a secret, so it is masked). `email` is the smallest one.

## What you add

Use a short, lowercase, stable key, for example `teams`. It is stored in `Destinations.ChannelKey`, so it never changes later.

### Backend

| Layer | File(s) to add | What it holds |
|---|---|---|
| Infrastructure | `Sonrisa.Infrastructure/Channels/<Name>/<Name>Channel.cs` | `INotificationChannel`: `Key`, `DisplayName`, `ValidateConfig`, `MaskConfig`, `DefaultConfig`, plus the config record (`SlackConfig(WebhookUrl)`) and static `Deserialize` |
| Infrastructure | `Sonrisa.Infrastructure/Channels/<Name>/<Name>Channel.Send.cs` (partial class; optional split) | `Format(NotificationContent)` and `SendAsync(configJson, message, ct)` |
| Infrastructure | `Sonrisa.Infrastructure/Configuration/ChannelOptions.cs` (only if needed) | An options class for settings of the channel, e.g. `SlackOptions.MockBaseUrl` |
| Tests | `Sonrisa.Tests/Unit/Channels/` | Validation, defaults and masking (copy `ChannelConfigTests`); format and status classification (copy `ChannelSendTests`) |

Namespaces you need: `Sonrisa.Application.Abstractions` (`INotificationChannel`, `ChannelContext`, `FormattedMessage`), `Sonrisa.Domain.Categories` (`FieldErrors`, `DataValidationResult`, `NotificationContent`), `Sonrisa.Domain.Entities` (`JsonText.Options`) and `Sonrisa.Domain.ValueObjects` (`SendResult`).

Rules for `<Name>Channel`:

- **`ValidateConfig(JsonElement, ChannelContext)`:** read the input with `FieldErrors.Read<T>(config, "config")`, add one error per bad field and return the normalized config JSON. A missing field takes the channel's default (email: the account email from `ChannelContext.AccountEmail`; Slack: a new local mock URL). The API reports the errors as `config.<field>`. Be strict: accept only what the channel can really send to (Slack: `https://hooks.slack.com/…` or the configured mock).
- **`MaskConfig(string)`:** the config as it may appear in **any** API response (AC18). Mask every secret (Slack: only the last 4 characters of the URL). It must not throw on a config this channel wrote. Responses, the admin view and the audit log only ever see this.
- **`DefaultConfig(ChannelContext)`:** what the create form starts with; already safe to show.
- **`Format(NotificationContent)`:** pure. Build the message from the content fields only (title, category label, key label and value, source, link, "SIMULATED" flag, why it matched, alert link, disclaimer). Never look at the category. Escape everything that comes from a feed (HTML-encode for HTML, Slack control characters for Slack) and put a link in the message only if it is `http` or `https`. A line break must not reach a header such as the email subject.
- **`SendAsync(configJson, message, ct)`:** one attempt, no loop. Return `SendResult.Sent()`, `SendResult.Retryable(error, retryAfter?)` (timeouts, connection errors, 429, 5xx, SMTP 4xx) or `SendResult.Permanent(error)` (the destination will keep refusing it). The error text is stored in `Deliveries.LastError` and shown in the admin log, so it **must not contain a secret** (not the webhook URL, not a password): name the cause (status code, exception type), not the raw exception message. Let `OperationCanceledException` propagate: the worker owns the timeout and turns it into a retry, and a host shutdown leaves the row pending. The worker (not the channel) counts attempts (max 3) and waits 10 s, then 60 s; a `Retry-After` replaces the delay, capped at 5 minutes.
- On edit, a request without `config` keeps the stored config. Your frontend form should send nothing for an untouched secret, because it only ever had the masked value.

### Frontend

| File to add | What it holds |
|---|---|
| `src/frontend/src/app/channels/<name>/<name>-config.ts` | `create<Name>ConfigForm(fb, start)` (start = the default config on create, the masked config on edit), `<name>ConfigData(form, editing)` (return `null` on edit to keep the stored config), a config form component with a required `form` input (and optional `editing`), and a summary component with a required `config` input |

## The one registration per side

**Backend:** one line in `Sonrisa.Api/Configuration/ChannelRegistration.cs`, under `// Channels`:

```csharp
services.AddSingleton<INotificationChannel>(new TeamsChannel(options, http));
```

(A channel that needs no outside client is registered as `AddSingleton<INotificationChannel, TeamsChannel>()`.)

**Frontend:** one entry in `src/frontend/src/app/channels/channel-registry.ts`:

```ts
teams: {
  configForm: TeamsConfigForm,
  createConfigForm: createTeamsConfigForm,
  configData: teamsConfigData,
  summary: TeamsConfigSummary,
},
```

No migration is needed: configs live in the existing `Destinations.Config` jsonb column.

## What you get without further changes

- The channel appears in the destination form with its default config (`GET /api/catalog`).
- Destination create, edit, delete, isolation by user, the per-user limit and audit work as for every channel (`DestinationService`); configs are masked in every response.
- Alerts can link destinations of the channel; matches queue `Pending` deliveries to them, the delivery worker sends them (`DeliveryWorker` → `DeliveryProcessor`) with a timeout, retries and the final `Sent` / `Failed` status, and the Notifications page and the admin delivery log show them with the channel's name.
- Deleting a destination unlinks it from the user's alerts and fails its pending deliveries ("destination deleted").

## Checklist

- [ ] The key is new: a duplicate key stops the app at startup (`ChannelRegistry`).
- [ ] No `switch` or `if` on channel keys outside the registries: `RegistryBoundaryTests` finds your `ChannelKey` constant by reflection and fails if the key is named anywhere but its definition (the `grep` in the README does the same by hand).
- [ ] Add the key to the test that lists the registered channels: `AlertsApiTests.The_catalog_lists_the_registered_categories_and_channels_with_defaults` fails until you do (the dry run found this).
- [ ] A secret never appears in a response: add your channel's secret to the AC18 test (`AlertsApiTests.No_response_contains_a_full_webhook_url`).
- [ ] Unit tests for validation, defaults, masking, formatting (escaping, SIMULATED) and status classification pass; no error text contains a secret; `dotnet test` and `npm run build` are green.
- [ ] Create a destination in the UI, link it to an alert, simulate a matching event; the Notifications page goes from `Pending` to `Sent`, and the message arrives.
