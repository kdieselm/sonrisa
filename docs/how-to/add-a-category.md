# How to add an event category

An **event category** is a kind of world event: disaster, news, later market (spec §2.2, §2.11). A category owns the shape of its event data, its simulate form, and (from S3b on) its alert rule, matching and message content. Core code never names a category: it finds one through the registry by key (ADR-2, AC11).

- **Status:** S3a. The data, simulate and list parts below are complete. S3b adds the rule, `Match` and `Describe`, and S5 adds stateful categories; both extend this guide.
- **Worked example:** `disaster` (USGS earthquakes). `news` is the smallest one.

## What you add

Use a short, lowercase, stable key, for example `weather`. It is stored in `Events.CategoryKey`, so it never changes later.

### Backend

| Layer | File(s) to add | What it holds |
|---|---|---|
| Domain | `Sonrisa.Domain/Categories/<Name>/<Name>Data.cs` | The record stored as the event's `Data` (jsonb), e.g. `DisasterData(Magnitude, Place, Latitude, Longitude, DepthKm)` |
| Domain | `Sonrisa.Domain/Categories/<Name>/<Name>Category.cs` | `IEventCategory`: `Key`, `DisplayName`, `MaxAge`, `ValidateSimulatedData`, plus static `Serialize` / `Deserialize` for the data record |
| Tests | `Sonrisa.Tests/Unit/Categories/` | Validation cases for the simulate data (copy `SimulatedDataTests`) |

Rules for `<Name>Category`:

- **`MaxAge`:** the age limit of spec §2.5. An event published more than this before it was first seen is stored but never notifies. Set it for feeds that repeat old items; use `null` when every event is fresh by nature.
- **`ValidateSimulatedData(JsonElement)`:** read the input with `FieldErrors.Read<T>`, add one error per bad field (the field name is the camelCase JSON name), and return `DataValidationResult.Valid(Serialize(...))`. The API reports the errors as `data.<field>`.
- **`Serialize` / `Deserialize`:** the only code that writes or reads this data shape. Sources call `Serialize`; nothing else touches the JSON.

### Frontend

| File to add | What it holds |
|---|---|
| `src/frontend/src/app/categories/<name>/<name>-simulate-form.ts` | `create<Name>SimulateForm(fb)`, `<name>SimulateData(form)` and a standalone component with a required `form` input |
| `src/frontend/src/app/categories/<name>/<name>-summary.ts` | A standalone component with a required `data` input that renders one line for the admin event list |

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
},
```

No migration is needed: event data lives in the existing `Events.Data` jsonb column.

## What you get without further changes

- The category appears in the admin event filters and in the simulate form (`GET /api/admin/events/filters`).
- Simulated events of the category go through the normal ingestion path and are audited.
- The admin list renders your summary component. If the frontend entry is missing, it falls back to raw JSON instead of failing.
- A source can now name the category (see [`add-a-source.md`](add-a-source.md)). A source of an unregistered category stops the app at startup.

## Checklist

- [ ] The key is new: a duplicate key stops the app at startup (`RegistryTests`).
- [ ] No `switch` or `if` on category keys outside the registries: `grep -rn '"weather"' src/` shows only the category class and its registry entry.
- [ ] Unit tests for the simulate validation pass; `dotnet test` and `npm run build` are green.
- [ ] Simulate one event in the admin view; it is listed with the "simulated" badge and your summary.
