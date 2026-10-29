# How to add an event category

An **event category** is a kind of world event: disaster, news, later market (spec §2.2, §2.11). A category owns the shape of its event data, its simulate form, its alert rule and matching, and (from S4 on) its message content. Core code never names a category: it finds one through the registry by key (ADR-2, AC11).

- **Status:** S3b. Data, simulate, list, rule, matching and the recent-matches flag below are complete. S4 adds `Describe` (message content), S5 stateful categories (market); both extend this guide.
- **Worked example:** `disaster` (USGS earthquakes; rule with magnitude, place text and an optional point + radius). `news` is the smallest one.

## What you add

Use a short, lowercase, stable key, for example `weather`. It is stored in `Events.CategoryKey` and `Alerts.CategoryKey`, so it never changes later.

### Backend

| Layer | File(s) to add | What it holds |
|---|---|---|
| Domain | `Sonrisa.Domain/Categories/<Name>/<Name>Data.cs` | The record stored as the event's `Data` (jsonb), e.g. `DisasterData(Magnitude, Place, Latitude, Longitude, DepthKm)` |
| Domain | `Sonrisa.Domain/Categories/<Name>/<Name>Rule.cs` | The record stored as an alert's `Rule` (jsonb), e.g. `DisasterRule(MinMagnitude, PlaceContains, Near)` |
| Domain | `Sonrisa.Domain/Categories/<Name>/<Name>Category.cs` | `IEventCategory`: `Key`, `DisplayName`, `MaxAge`, `ValidateSimulatedData`, `ValidateRule`, `Matches`, `SupportsBackfill`, `BackfillUnavailableReason`, plus static `Serialize` / `Deserialize` for the data and the rule |
| Tests | `Sonrisa.Tests/Unit/Categories/` | Validation of the simulate data (copy `SimulatedDataTests`) and of the rule, and matching with its boundaries (copy `RuleMatchingTests`) |

Rules for `<Name>Category`:

- **`MaxAge`:** the age limit of spec §2.5. An event published more than this before it was first seen is stored but never notifies. Set it for feeds that repeat old items; use `null` when every event is fresh by nature.
- **`ValidateSimulatedData(JsonElement)`:** read the input with `FieldErrors.Read<T>`, add one error per bad field (the field name is the camelCase JSON name), and return `DataValidationResult.Valid(Serialize(...))`. The API reports the errors as `data.<field>`.
- **`ValidateRule(JsonElement)`:** the same pattern for the alert rule, with `FieldErrors.Read<T>(rule, "rule")`. Return the rule **normalized** (trimmed, duplicates dropped), because the alert compares rules to decide whether a change resets its "active since" time. The API reports the errors as `rule.<field>`.
- **`Matches(Event, string ruleJson)`:** pure code: no clock, no database. The ingestor applies "active since" and the age limit before it calls you, and catches a rule or event your code cannot read (that alert is skipped and logged, the others still match). Test the boundaries: the exact threshold matches, just below does not.
- **`SupportsBackfill`:** true if a past event says something about the alert (disaster, news), so the alert page can list up to 3 recent matches. False if events are relative to an earlier state (market); then give a one-sentence `BackfillUnavailableReason`.
- **`Serialize` / `Deserialize`:** the only code that writes or reads these shapes. Sources call `Serialize`; nothing else touches the JSON.

### Frontend

| File to add | What it holds |
|---|---|
| `src/frontend/src/app/categories/<name>/<name>-simulate-form.ts` | `create<Name>SimulateForm(fb)`, `<name>SimulateData(form)` and a standalone component with a required `form` input |
| `src/frontend/src/app/categories/<name>/<name>-summary.ts` | A standalone component with a required `data` input that renders one line of an event (admin list, recent matches) |
| `src/frontend/src/app/categories/<name>/<name>-rule.ts` | `create<Name>RuleForm(fb, rule)` (rule is the stored rule when editing, `null` for a new alert), `<name>RuleData(form)`, a rule form component with a required `form` input, and a rule summary component with a required `rule` input |

## The one registration per side

**Backend:** one line in `Sonrisa.Api/Configuration/SourceRegistration.cs`, under `// Categories`:

```csharp
services.AddSingleton<IEventCategory, WeatherCategory>();
```

**Frontend:** one entry in `src/frontend/src/app/categories/category-registry.ts`:

```ts
weather: {
  simulateForm: WeatherSimulateForm,
  createSimulateForm: createWeatherSimulateForm,
  simulateData: weatherSimulateData,
  summary: WeatherSummary,
  ruleForm: WeatherRuleForm,
  createRuleForm: createWeatherRuleForm,
  ruleData: weatherRuleData,
  ruleSummary: WeatherRuleSummary,
},
```

No migration is needed: event data lives in `Events.Data` and rules in `Alerts.Rule`, both existing jsonb columns.

## What you get without further changes

- The category appears in the admin event filters and in the simulate form (`GET /api/admin/events/filters`), and in the alert form (`GET /api/catalog`).
- Simulated events of the category go through the normal ingestion path and are audited.
- Every new event of the category is matched at ingestion against the enabled alerts of the category; each match queues one `Pending` delivery per destination in the same transaction as the event (`EventIngestor`, architecture §3.1). Revisions never match again.
- Alert create, edit, enable/disable, delete, isolation by user and audit work as for every category (`AlertService`).
- The alert page lists recent matches through your `Matches`, or shows your reason if `SupportsBackfill` is false.
- The admin list, the alert list and the admin user view render your summary components. If the frontend entry is missing, the category is left out of the forms instead of failing.
- A source can now name the category (see [`add-a-source.md`](add-a-source.md)). A source of an unregistered category stops the app at startup.

## Checklist

- [ ] The key is new: a duplicate key stops the app at startup (`RegistryTests`).
- [ ] No `switch` or `if` on category keys outside the registries: `grep -rn '"weather"' src/` shows only the category class and its registry entry.
- [ ] Unit tests for the simulate data, the rule validation and the matching boundaries pass; `dotnet test` and `npm run build` are green.
- [ ] Create an alert of the category, simulate a matching and a non-matching event in the admin view; only the matching one appears on the Notifications page.
