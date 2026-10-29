# Specification

- **Status:** revised after review (phase 2). The raw AI draft is commit `bf14840`; the diff to this version shows the changes. Review findings and decisions: [`process/log.md`](../process/log.md), entries from 2026-10-29 16:00 (review) and D14–D16.
- **Tool / model:** Claude Code, Claude Opus 5.5 (session 03); review by a fresh subagent with [`prompts/templates/challenge.md`](../prompts/templates/challenge.md).
- **Inputs:** [`brief.md`](brief.md), [`brainstorm/03-triage.md`](brainstorm/03-triage.md) (final), [`../process/log.md`](../process/log.md) (D1–D16).
- **Rule:** built only from triage rows with the final verdict *accept*, plus the logged decisions D14–D16 taken during the spec review. Every statement cites its row (T…) or decision (D…). *Defer* rows appear only in §4 (out of scope). Facts that are not verified appear only in §8.

## 1. Requirements traceability

Every row maps a phrase of the brief to how it is built and how it is checked. No row may be dropped or weakened (CLAUDE.md rule 1).

| Brief | Requirement | Implementation | Verification |
|---|---|---|---|
| "users to be able to…" | User management (D2, T53, T57) | Self-registration, login, own `/logout`; role `Admin` for admins, every other account is a normal user; every alert and destination belongs to one user; users see only their own | AC2, AC3, AC4 |
| "set up alerts" | Alert management (T80) | Create, list, edit, enable/disable, delete; one rule per alert in the category's native units (§2.2) | AC5 |
| "get notified when something important happens" | Importance and timeliness (T3, T7, T27, T34, T38, D14, D15) | "Important" = the user's own rule in native units (magnitude, % move, headline keywords) above a source-level floor; real-time within one poll interval; no notifications for events from before the alert was active | AC6, AC7, AC8 |
| "in the world — like breaking news, market movements, natural disasters" | All three categories from real sources (T14, T15, T17, T20, T22) | Disaster: USGS `4.5_day` GeoJSON. News: BBC World and Guardian World RSS. Market: CoinGecko crypto (keyless), Finnhub stocks if `FINNHUB_API_KEY` is set, else a mock stock source. Plus a labeled simulated injector (admin) | AC9 |
| "Should work for both email and Slack" | Two channels (T63, T65, T67, T68) | Email via SMTP with MailKit (Mailpit locally); Slack incoming webhook (local mock receiver by default, real webhook optional) | AC1, AC10, AC19, AC20 |
| "flexible enough that we can add more channels later" | Three extension points (D10, T11, T58, T85) | Notification channel, event category, source adapter: each is one backend interface + one registration and one frontend registry entry; "How to add" guide for each | AC11 |
| "We need an admin view too" | Admin interface (T42, T49, T50, T51) | Seeded admin; read-only view of users and their alerts, events, delivery log, audit log, Slack mock messages; "simulate event" form | AC3, AC12 |

## 2. Assumptions and domain model

### 2.1 Product context

- Standalone web application; no existing product, stack or user base is assumed (T56).
- English-language, global sources (T84).
- Runs locally with `docker compose up`; the reviewer needs no keys or accounts (T25, own addition in `brief.md`).

### 2.2 Importance: per-category rules in native units (T3, D9)

There is no global importance score (T2 rejected). Each alert has exactly one category and a rule in that category's units. One category per alert keeps the rule form and the matcher per category (T85).

| Category | Rule fields | Matches when |
|---|---|---|
| Disaster | `minMagnitude` (≥ 4.5, the feed floor), optional `placeContains` (text) | `event.magnitude ≥ minMagnitude` and, if set, `event.place` contains `placeContains` (case-insensitive substring) (T75) |
| Market | `asset` (one of the configured assets), `direction` (up / down / either), `thresholdPercent` (≥ 1, multiple of 0.5), optional `stepPercent` (multiple of 0.5) | see §2.4 (re-arm and steps, D14) |
| News | `keywords` (1–10, at least one required) | any keyword or phrase occurs in the **headline** at word boundaries, case-insensitive (culture-invariant) (D15). The summary is not searched |

**Source-level floor (T7):** sources limit what is ingested; there is no admin-tunable global threshold (T83). Disaster: the USGS `4.5_day` feed (magnitude ≥ 4.5). Market: alert thresholds start at 1 %, and market events are emitted only on 0.5-point grid crossings (§2.4). News: every feed item is ingested; noise is limited by headline-only matching (D15) and is a known limit (§7).

### 2.3 Events (T14, T22, T31)

`Event`: id, category, source, external ID, title, summary, URL, published time, first-seen time, updated time, `isSimulated`, and a category-specific data object (JSONB): disaster = magnitude, place; market = asset, previous change %, current change %, baseline description; news = feed name.

- **Item dedup (T31):** unique `(source, externalId)`; re-ingestion updates the row (upsert). RSS: external ID = `guid`, else `link`; an item without `pubDate` gets published time = first-seen time.
- **Revisions (T9):** a revised event updates the row but is **not** matched again, so it never causes a second notification. Consequence: a quake first ingested at 4.6 and revised to 6.0 never notifies a 6.0 alert (known limit, §7).
- **Simulated events (T14):** created by the admin's "simulate event" form for any category (market: asset, previous and current change %); `isSimulated = true`, shown as "simulated" in the admin view and in every notification. Published and first-seen time = creation time. They go through the same matching.

### 2.4 Market movements (T22, T35, D14)

- The movement is the provider's own change field; no own price history (T22): CoinGecko `usd_24h_change` (rolling 24 hours); Finnhub `dp` (baseline unverified, §8); the mock produces the same shape.
- The source keeps the last observed change per asset. On each poll, if the change crossed at least one **0.5-point grid line** since the previous observation, it emits one market event (external ID = asset + poll time) with the previous and current change. No event on the first poll after start (no previous value).
- Each market alert keeps a small state per direction: `armed` and `lastNotifiedLevel`. For the **up** direction (down is the mirror image, using negative values; "either" tracks both directions separately):
  - **Fire:** armed and `current ≥ threshold` → notify; disarm; `lastNotifiedLevel` = the highest level `threshold + k·step` that `current` reached (k ≥ 0; without a step, `threshold`).
  - **Escalate:** disarmed, a step is set and `current ≥ lastNotifiedLevel + step` → notify again; update `lastNotifiedLevel`.
  - **Re-arm (hysteresis):** `current < threshold − 1` → armed again, `lastNotifiedLevel` cleared. Values between `threshold − 1` and `threshold` change nothing, so a price hovering at the threshold does not flap.
  - **Start state:** when an alert becomes active (§2.5), it starts disarmed if the latest observed change is already past its threshold, otherwise armed. So it fires only on a new crossing, never for a move that was already under way (T34).
- Example (threshold 5, step 2.5): 2.0 → 5.2 notifies; 5.2 → 5.8 nothing; → 7.6 notifies (level 7.5); → 4.5 nothing; → 3.9 re-arms; → 5.1 notifies.
- The poll interval stays 5 minutes for CoinGecko and Finnhub (keyless CoinGecko limit is unpublished, F1); the mock moves every minute.

### 2.5 Sources, matching and timing (T14–T27, T34, T38, T71, D8)

Every source implements one source-adapter interface and is registered once (T85). A failing source logs the error and does not stop the other sources or delivery (T26).

| Category | Source | Endpoint | Poll interval |
|---|---|---|---|
| Disaster | USGS earthquakes | `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/4.5_day.geojson` (F6) | 1 min |
| News | BBC World RSS | `https://feeds.bbci.co.uk/news/world/rss.xml` (F7) | 5 min |
| News | Guardian World RSS | `https://www.theguardian.com/world/rss` (F7) | 5 min |
| Market | CoinGecko (crypto, keyless; optional Demo key) | `GET /api/v3/simple/price?ids=…&vs_currencies=usd&include_24hr_change=true`, one batched call (F1) | 5 min |
| Market | Finnhub (stocks, only if `FINNHUB_API_KEY` is set) | `/quote` per symbol, `dp` field (F9) | 5 min |
| Market | Mock stocks (if no Finnhub key) | generated moves, `isSimulated = true` | 1 min |
| any | Simulated injector | admin form | on demand |

Assets: crypto `bitcoin`, `ethereum`, `solana`; stocks `AAPL`, `MSFT`, `NVDA`, `TSLA`, `AMZN`. The list is configuration, not code. If a real source becomes unusable during the build, only that source falls back to a simulated one, and the fallback is logged (T20).

- One `BackgroundService` polls each source on its interval; matching runs at ingestion (T27). It loops over the enabled alerts of the event's category (T71; limit in §7).
- **Active since:** every alert has `activeSince` = the time of creation, of the last re-enable, or of the last rule change. An alert matches only events first seen after `activeSince` (T34, D9). Adding a destination to an alert does not deliver earlier matches to it.
- **Age limit (T34):** a disaster or news event published more than **2 hours** before ingestion is stored but never notified. Market events are observations at poll time and are never older than one interval.
- **In-app back-fill (D11):** the alert page lists up to **3** already-ingested events from the **last 24 hours** that match the alert, for disaster and news alerts only. Nothing is sent and no delivery rows are created. Market alerts show an empty list with a short reason (no price history, T22).

### 2.6 Destinations and channels (T58, T63–T70)

- A **destination** belongs to a user: type (channel key), display name, config (JSONB). One alert links to one or more destinations, of any channels (T67).
- **Channel contract (T58):** `validate(config)`, `render(event, alert)`, `send(destination, message)`. Core code never branches on the channel type (T59).
- **Email (T65, T66, D13):** config = address, defaulting to the account email (copied at creation). Sent with MailKit over SMTP; host and port from env (Mailpit locally). HTML body.
- **Slack (T63, T64, T70):** config = incoming webhook URL. The message is plain text that contains the same fields as the email (T68). A new Slack destination defaults to the local mock receiver. A real webhook URL can be entered per destination, or set for the seeded demo user via `SLACK_WEBHOOK_URL`. The webhook URL is a secret: never logged, masked in every API response (only the last 4 characters are shown) and never written to the audit log.
- **Slack mock receiver (T63):** enabled by configuration (on in Compose), anonymous like a real webhook:
  - `POST /mock/slack/hooks/{name}` → 200, stores the message for the admin view;
  - `POST /mock/slack/fail` → 500 (retry path);
  - `POST /mock/slack/reject` → 404 (final-failure path).
  The default URL uses the API's internal Compose address, so the worker reaches it from inside the network.
- **Validation (T69):** each channel validates its config on create and update. Email: address format. Slack: `https://hooks.slack.com/…` or a URL under the configured mock base URL.
- **Deleting a destination:** soft delete; it is unlinked from alerts, its pending deliveries become `Failed` ("destination deleted"); history stays.
- **Message content (T76, T77, T78):** title, category, key value (magnitude / previous → current % move / matched keywords), source, link, "SIMULATED" label if set, why it matched (alert name and rule), a link to the alert page in the app, and one disclaimer line per category ("not an official warning" for disasters, "not financial advice; data may be delayed" for market).

### 2.7 Delivery (T33, T36, T64, T73)

- A match creates one `Delivery` row per (alert, event, destination) with status `Pending`. Unique index on `(alertId, eventId, destinationId)` prevents double delivery. T33 says "channel"; the key is the destination because T67 allows several destinations per channel, and a channel key would drop the second one (D16).
- A `BackgroundService` worker sends pending rows. Statuses: `Pending` → `Sent` | `Failed`. At most **3 attempts**; delays between attempts 10 s and 60 s. A `Retry-After` header replaces the delay, capped at 5 minutes; it still counts as an attempt. One failing send does not block others (T36).
- **Slack errors (D13):** 429, 5xx and timeouts are retried; any other 4xx is a final failure at once.
- Each attempt stores the attempt count and last error (no secrets).

### 2.8 Users, roles and auth (D2, D5, T53, T55, T57, T51)

- ASP.NET Core Identity API endpoints (`MapIdentityApi`) for register, login and refresh. One role, `Admin`; an account without it is a normal user. No code needs to run inside `/register` to assign a role.
- **Seeded admin (T51):** created at startup from `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` if it does not exist.
- **Logout (T57, D9):** own `POST /logout` updates the user's security stamp, so every existing refresh token gets 401 at `/refresh` (F8). The client discards its tokens. The bearer access token lifetime is set to **10 minutes**; an access token issued before logout keeps working until it expires (known limit, §7).
- **Audit hooks for register and login:** an endpoint filter on the Identity route group writes the audit entry from the result status. Whether this works is unverified (§8); the fallback is own `/register` and `/login` endpoints that mirror the framework handlers.
- **Unused Identity endpoints (T55):** `MapIdentityApi` also maps `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa`, `/manage/info` (F8). Email confirmation, password reset and 2FA are out of scope; the same filter answers 404 for these paths, so `/manage/info` cannot change the account email either.
- **Isolation:** every user endpoint filters by the caller's user ID; another user's alert or destination returns **404** (not 403, so IDs are not confirmed).

### 2.9 Admin view (T42, T49, T50)

Read-only, `Admin` role only:

- users, and each user's alerts and destinations (webhook URLs masked);
- events, filterable by category and source, with the "simulated" label;
- delivery log: alert, event, destination, status, attempts, last error;
- Slack mock messages;
- audit log;
- **simulate event** form: category, title, category-specific fields; the event is created and matched like any other.

### 2.10 Audit log (T50, D12)

Append-only table: time, acting user, action, target type and ID, outcome. Actions: register, login success and failure, logout, alert create/update/delete/enable/disable, destination create/update/delete, admin "simulate event". No secrets (no passwords, tokens or webhook URLs). Written by one service called from the endpoints. Read-only list in the admin view.

### 2.11 Extension points (T85, D10, T11)

| Extension point | Backend | Frontend | Guide |
|---|---|---|---|
| Notification channel | channel interface (§2.6) + one DI registration | registry entry: channel key → destination config form | `docs/how-to/add-a-channel.md` |
| Event category | category definition: rule shape, validator, matcher, data shape + one registration | registry entry: category key → rule form and event detail view | `docs/how-to/add-a-category.md` |
| Source adapter | source interface (fetch → normalized events) + one registration and config section | none needed unless it adds a category | `docs/how-to/add-a-source.md` |

Adding a type touches only the new files and one registration per side. No `switch` or `if` on channel, category or source type outside the registries (T59, T85). Each guide lists the exact files a new type adds and the one registration line it changes.

## 3. MVP scope

- Register, login, logout (own `/logout`, 10-minute access token)
- Destinations: email and Slack, with validation; webhook URLs masked
- Alert CRUD with enable/disable; per-category rules (§2.2); market re-arm and steps (§2.4); one or more destinations per alert
- Ingestion: USGS, BBC and Guardian RSS, CoinGecko, Finnhub (optional) or mock stocks, simulated injector
- Matching at ingestion; no back-fill notifications; in-app list of up to 3 recent matches (disaster, news)
- Delivery worker: email (MailKit → Mailpit), Slack (mock or real webhook), max 3 attempts, unique delivery index
- Admin view: users and alerts, events, delivery log, Slack mock messages, audit log, simulate event
- Audit log
- Three extension points with three "how to add" guides
- `docker compose up` runs Postgres, Mailpit, API and frontend
- xUnit tests for the acceptance criteria marked "test"; CI (build + test)

## 4. Out of scope

Each item is a *defer* row of the triage; the reason is in that row.

| Item | Row | Reason (short) |
|---|---|---|
| More disaster sources (GDACS, NASA EONET, NWS) | T16 | One real source per category meets the brief |
| GDELT news | T18 | Coded events, not headlines; RSS is simpler |
| Free-form "what would this alert have matched" preview | T10 | Only the limited list of §2.5 is in scope |
| Digests, per-alert digest option, throttling | T40 | Not asked; storm risk limited by T7, D14 and D15 |
| Quiet hours, time zones, hourly caps | T41 | Not in the brief |
| Admin actions (pause any alert, retry a delivery) | T43 | Read-only admin meets the brief |
| Source health in the admin view | T44 | First item to add if time allows |
| Email verification, password reset, SSO, magic link | T55 | Not in the brief |
| Generic outbound webhook channel | T60 | A third channel is not asked; the guide covers it |
| "Send test message" for a destination | T69 | Extra UI |
| Encryption of webhook URLs at rest | T70 | Masking only in the MVP |
| Retention of events and delivery history | T74 | Not in the brief |
| One-click unsubscribe from a message | T77 | Needs signed tokens; the message links to the alert page |
| User-facing delivery status, auto-pause of broken destinations | T79 | Admin sees failures |
| User-facing notification history | T81 | Not asked |

## 5. Acceptance criteria

"Test" = automated xUnit test. "Manual" = step in the README review path. Every criterion states the result that makes it fail.

| # | Criterion | How |
|---|---|---|
| AC1 | End-to-end: admin simulates an event → a matching alert of a normal user with an email and a Slack destination → exactly one email in Mailpit and exactly one message in the Slack mock list | Manual (required); automated as well if the Mailpit API is verified in M3 (§8) |
| AC2 | Register, login and logout work; after logout, `/refresh` with the old refresh token returns 401; the configured access-token lifetime is 10 minutes | Test |
| AC3 | A normal user gets 403 and an anonymous caller gets 401 on **every** admin endpoint; the test enumerates the admin routes from the app's endpoint list, so a new admin endpoint is covered without editing the test | Test |
| AC4 | Reading, editing, deleting or linking another user's alert or destination by ID returns 404 and changes nothing | Test |
| AC5 | Alert create, list, edit, enable/disable and delete work through the API; a disabled alert does not match; re-enabling resets `activeSince`. The UI offers the same five actions | Test + manual |
| AC6 | Matching per category, including boundaries: magnitude equal to the minimum matches, 0.1 below does not; place text is case-insensitive; a keyword matches as a whole word in the headline (case-insensitive), not inside another word and not in the summary only | Test (unit) |
| AC7 | An event first seen before `activeSince`, or a disaster or news event published more than 2 h before ingestion, creates no delivery | Test |
| AC8 | Market re-arm and steps: the sequence of §2.4 (threshold 5, step 2.5) produces exactly 3 notifications at the documented points; an alert created while the change is already past its threshold does not notify until it has re-armed | Test (unit) |
| AC9 | On a clean start, the admin event list shows real events from USGS and from at least one RSS feed within 10 minutes; the CoinGecko adapter parses a recorded real response into market observations; a simulated event shows the "simulated" label in the admin list and "SIMULATED" in the rendered message | Manual + test |
| AC10 | Slack failure path: `/mock/slack/fail` (500) → 3 attempts → `Failed` with the last error; `/mock/slack/reject` (404) → `Failed` after 1 attempt | Test |
| AC11 | No `switch`/`if` on channel, category or source keys outside the registries: a documented `grep` in the README returns no hits; each "how to add" guide lists the files it adds and the one registration it changes | Review check (procedure in the README) |
| AC12 | Audit log: an alert change and a logout each write exactly one entry; a normal user gets 403 on the audit endpoint; no webhook URL appears in any entry | Test |
| AC13 | The same event is never delivered twice to the same alert and destination: a duplicate insert is rejected by the unique index | Test |
| AC14 | The alert page lists at most 3 matching events from the last 24 h for disaster and news alerts, creates no delivery rows, and is empty for market alerts | Test |
| AC15 | `docker compose up` works on a clean clone with only `.env.example` copied to `.env` | Manual (clean clone) |
| AC16 | Re-ingesting the same item updates the event row (upsert) and creates no new event and no new delivery (T9, T31) | Test |
| AC17 | A source that throws does not stop the other sources' polling or the delivery worker (T26) | Test |
| AC18 | No API response contains a full webhook URL (T70) | Test |
| AC19 | A sample email and a sample Slack mock message show title, key value, source, link, why it matched and the category disclaimer (T76, T78) | Manual (screenshots in `process/screenshots/`) |
| AC20 | Optional: one end-to-end run with a real Slack incoming webhook from my own workspace; screenshot in `process/screenshots/`. Not required from the reviewer | Manual, optional |

## 6. Cut list (only if time runs short, in this order)

Cutting an item also drops the acceptance criteria that depend on it (named below); every cut is logged.

1. Styling and UI polish
2. Audit log (D12; not a brief requirement) — drops AC12
3. CI workflow
4. Finnhub adapter (mock stocks stay; CoinGecko stays real)
5. Delivery log filters and details (the log itself stays)
6. In-app back-fill list (D11) — drops AC14
7. Containerizing the apps: infrastructure stays in Compose, API and frontend start from the README — AC15 then covers the README steps
8. Last resort, **only with my explicit approval:** all three categories come from a simulated source instead of real feeds. This **weakens** the brief's "something important happens **in the world**" (T13 rejected simulated-only for that phrase), so it would be recorded as a requirement weakened under time pressure, not as a cut inside the brief (D16)

**Never cut:** user management (register, login, logout), alert CRUD, all three event categories, email and Slack, the three extension points and their guides, admin view, this spec, `process/log.md` with real entries, milestone commits, prompt history, a working end-to-end path.

## 7. Known limits

- **Logout window:** an access token issued before logout stays valid for up to 10 minutes (T57).
- **Scale:** matching loops over all enabled alerts of a category per event; fine for a demo, not for large numbers of alerts (T71).
- **Region:** disaster place filter is a text match on USGS `place`, not geographic (T75); no region filter for news and market.
- **News noise:** a common keyword (for example a country name) can still produce many notifications; two feeds and no cross-source dedup (T32) mean the same story can notify twice. Throttling and digests are out of scope (T40).
- **Disaster revisions:** a revised magnitude is not matched again (T9), so an upgrade across an alert's threshold does not notify.
- **Market baseline:** CoinGecko's change is a rolling 24 h value; Finnhub's `dp` baseline is unverified (§8). A "move" is the provider's number, not our own calculation. Grid crossings between two polls are seen only at poll time.
- **Unconfirmed email addresses:** email destinations accept any address, and email confirmation is out of scope, so a user could direct notifications to someone else's inbox.
- **Feed terms of use not reviewed (T24):** notifications carry only title, link and source name, no article text.
- **Slack:** incoming webhook posts to one channel; no DMs (T63). The automated tests use the mock; a real webhook is exercised only in the optional AC20.
- **No retention policy** (T74).

## 8. Unverified facts (must be checked before the named milestone)

| Fact | Status | Check before |
|---|---|---|
| The bearer token from `MapIdentityApi` carries the role claim, so `[Authorize(Roles = "Admin")]` works | Unverified (CLAUDE.md risk list) | M1 (AC3) |
| An endpoint filter on the `MapIdentityApi` route group can see the result of `/register` and `/login` and short-circuit unused paths | Unverified | M1 (fallback: own endpoints) |
| Option name and effect for the 10-minute access-token lifetime | Partly verified (F8: 1 h default) | M1 (AC2) |
| USGS publication delay relative to event time (does the 2 h window drop real quakes?) | Unverified | M2 |
| CoinGecko keyless rate limit and monthly cap | Not published (F1) | M2 (live call; fall back to mock if blocked) |
| Finnhub free per-minute limit | Unverified (F2) | M2 (real free key) |
| Finnhub `dp` baseline (previous close?) | Unverified (F9) | M2 (live call vs. `pc`) |
| Mailpit REST API paths (for an automated AC1) | Unverified (F11) | M3 |
| Slack incoming webhook 4xx codes | Unverified (D13) | M3 |
| Terms of use of BBC, Guardian, CoinGecko, Finnhub | Not reviewed (T24) | Accepted as a known risk |
