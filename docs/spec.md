# Specification

- **Status:** raw AI draft (phase 2). Not yet reviewed.
- **Tool / model:** Claude Code, Claude Opus 5.5 (session 03).
- **Inputs:** [`brief.md`](brief.md), [`brainstorm/03-triage.md`](brainstorm/03-triage.md) (final), [`../process/log.md`](../process/log.md) (D1–D13).
- **Rule:** built only from triage rows with the final verdict *accept*. Every statement cites its row (T…) or decision (D…). *Defer* rows appear only in §4 (out of scope). Facts that are not verified appear only in §8.
- **⚑** marks a value or rule the triage left open and this draft proposes. Each needs the user's decision.

## 1. Requirements traceability

Every row maps a phrase of the brief to how it is built and how it is checked. No row may be dropped or weakened (CLAUDE.md rule 1).

| Brief | Requirement | Implementation | Verification |
|---|---|---|---|
| "users to be able to…" | User management (D2, T53, T57) | Self-registration, login, own `/logout`; roles `User` and `Admin`; every alert and destination belongs to one user; users see only their own | AC2, AC3, AC4 |
| "set up alerts" | Alert management (T80) | Create, list, edit, enable/disable, delete; one rule per alert in the category's native units (§2.2) | AC5 |
| "get notified when something important happens" | Importance and timeliness (T3, T7, T27, T34, T38) | "Important" = the user's own rule in native units (magnitude, % move, keywords) above a source-level floor; real-time within one poll interval; no notifications for events older than the alert | AC6, AC7, AC8 (matching unit tests) |
| "in the world — like breaking news, market movements, natural disasters" | All three categories from real sources (T14, T15, T17, T20, T22) | Disaster: USGS `4.5_day` GeoJSON. News: BBC World and Guardian World RSS. Market: CoinGecko crypto (keyless), Finnhub stocks if `FINNHUB_API_KEY` is set, else a mock stock source. Plus a labeled simulated injector (admin) | AC9; admin event list shows events per category and source |
| "Should work for both email and Slack" | Two channels (T63, T65, T67) | Email via SMTP with MailKit (Mailpit locally); Slack incoming webhook (local mock receiver by default) | AC1, AC10 |
| "flexible enough that we can add more channels later" | Three extension points (D10, T58, T85) | Notification channel, event category, source adapter: each is one backend interface + one registration and one frontend registry entry; "How to add" guide for each | AC11 (review check + guides) |
| "We need an admin view too" | Admin interface (T42, T49, T50, T51) | Seeded admin; read-only view of users and their alerts, events, delivery log, audit log, Slack mock messages; "simulate event" form | AC3 (403 for normal users), AC12 |

## 2. Assumptions and domain model

### 2.1 Product context

- Standalone web application; no existing product, stack or user base is assumed (T56).
- English-language, global sources (T84).
- Runs locally with `docker compose up`; the reviewer needs no keys or accounts (T25, own addition in `brief.md`).

### 2.2 Importance: per-category rules in native units (T3, D9)

There is no global importance score (T2 rejected). Each alert has exactly one category ⚑ and a rule in that category's units:

| Category | Rule fields | Matches when |
|---|---|---|
| Disaster | `minMagnitude` (≥ 4.5, the feed floor), optional `placeContains` (text) | `event.magnitude ≥ minMagnitude` and, if set, `event.place` contains `placeContains` (case-insensitive substring) (T75) |
| Market | `asset` (one of the configured assets), `minChangePercent` (≥ the source floor), `direction` (up / down / either) ⚑ | the event is for `asset`, its direction is allowed, and `|changePercent| ≥ minChangePercent` |
| News | `keywords` (1–10, at least one required) | any keyword occurs in the title or summary as a whole word, case-insensitive ⚑ |

**Source-level floor (T7):** sources limit what is ingested; there is no admin-tunable global threshold (T83). Disaster: the USGS `4.5_day` feed (magnitude ≥ 4.5). Market: a market event is created only when `|change| ≥ 1 %` ⚑. News: every feed item is ingested.

### 2.3 Events (T14, T22, T31, T35)

`Event`: id, category, source, external ID, title, summary, URL, published time, first-seen time, updated time, `isSimulated`, and a category-specific data object (JSONB): disaster = magnitude, place; market = asset, change percent, direction, baseline description; news = feed name.

- **Item dedup (T31):** unique `(source, externalId)`; re-ingestion updates the row (upsert).
- **Revisions (T9):** a revised disaster or news event updates the row but is **not** matched again, so it never causes a second notification.
- **Market movement (T22, T35):** the movement is the provider's own change field, no own price history: CoinGecko `usd_24h_change` (rolling 24 hours); Finnhub `dp` (baseline unverified, §8); the mock produces the same shape. A market event is one **crossing per asset × direction × UTC day** ⚑ (external ID such as `bitcoin:up:2026-10-29`). Each poll updates the change value and **re-matches** the event ⚑ (needed so that a 5 % alert fires when a move that started at 2 % reaches 5 %); the delivery unique index stops repeats.
- **Simulated events (T14):** created by the admin's "simulate event" form for any category; `isSimulated = true`, shown as "simulated" in the admin view and in every notification. They go through the same matching.

### 2.4 Sources (T14–T22, D8)

Every source implements one source-adapter interface and is registered once (T85). A failing source logs the error and does not stop the other sources or delivery (T26).

| Category | Source | Endpoint | Poll interval ⚑ |
|---|---|---|---|
| Disaster | USGS earthquakes | `https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/4.5_day.geojson` (F6) | 1 min |
| News | BBC World RSS | `https://feeds.bbci.co.uk/news/world/rss.xml` (F7) | 5 min |
| News | Guardian World RSS | `https://www.theguardian.com/world/rss` (F7) | 5 min |
| Market | CoinGecko (crypto, keyless; optional Demo key) | `GET /api/v3/simple/price?ids=…&vs_currencies=usd&include_24hr_change=true`, one batched call (F1) | 5 min |
| Market | Finnhub (stocks, only if `FINNHUB_API_KEY` is set) | `/quote` per symbol, `dp` field (F9) | 5 min |
| Market | Mock stocks (if no Finnhub key) | generated moves, `isSimulated = true` | 1 min |
| any | Simulated injector | admin form | on demand |

Assets ⚑: crypto `bitcoin`, `ethereum`, `solana`; stocks `AAPL`, `MSFT`, `NVDA`, `TSLA`, `AMZN`. The list is configuration, not code.

If CoinGecko fails during the build, the market category falls back to the mock source and the fallback is logged (T20).

### 2.5 Matching and timing (T27, T34, T38, T71)

- One `BackgroundService` polls each source on its interval; matching runs at ingestion (T27).
- Matching loops over the enabled alerts of the event's category (T71: demo scale, limit in §7).
- **No back-fill notifications (T34, D9):** an alert matches only events first seen after the alert was created; an event published more than **2 hours** ⚑ before ingestion is stored but never notified.
- **In-app back-fill (D11):** the alert page lists up to **3** already-ingested events from the **last 24 hours** that match the alert, for disaster and news alerts only. Nothing is sent and no delivery rows are created. Market alerts show an empty list with a short reason.

### 2.6 Destinations and channels (T58, T63–T70)

- A **destination** belongs to a user: type (channel key), display name, config (JSONB). One alert links to one or more destinations, of any channels (T67).
- **Channel contract (T58):** `validate(config)`, `render(event, alert)`, `send(destination, message)`. Core code never branches on the channel type (T59).
- **Email (T65, T66, D13):** config = address, defaulting to the account email. Sent with MailKit over SMTP; host and port from env (Mailpit locally). HTML body.
- **Slack (T63, T64, T70):** config = incoming webhook URL. A new Slack destination defaults to the local mock receiver (`POST /mock/slack/{name}`), which stores each message for the admin view. A real webhook URL can be entered per destination, or set for the seeded demo user via `SLACK_WEBHOOK_URL`. The webhook URL is a secret: never logged, masked in API responses and in the audit log.
- **Validation (T69):** each channel validates its config on create and update (email format; Slack URL is `https://hooks.slack.com/…` or the local mock URL ⚑).
- **Message content (T76, T78, T77):** title, category, key value (magnitude / % move / matched keywords), source, link, "simulated" label if set, why it matched (the alert name and rule), a link to the alert page in the app, and one disclaimer line per category ("not an official warning" for disasters, "not financial advice; data may be delayed" for market).

### 2.7 Delivery (T33, T36, T64, T73)

- A match creates one `Delivery` row per (alert, event, **destination**) ⚑ with status `Pending`. Unique index on `(alertId, eventId, destinationId)` prevents double delivery (T33; the triage says "channel", but with several destinations per channel (T67) the key must be the destination).
- A `BackgroundService` worker sends pending rows. Statuses: `Pending` → `Sent` | `Failed`. At most **3 attempts**; delays between attempts 10 s and 60 s ⚑. One failing send does not block others (T36).
- **Slack errors (D13):** 429, 5xx and timeouts are retried, respecting `Retry-After`; any other 4xx is a final failure at once. The mock's `/mock/slack/fail` returns 500 so the retry path is testable.
- Each attempt stores the attempt count and last error (no secrets).

### 2.8 Users, roles and auth (D2, D5, T53, T57, T51)

- ASP.NET Core Identity API endpoints (`MapIdentityApi`) for register, login and refresh; roles `User` (default on register) and `Admin`.
- **Seeded admin (T51):** created at startup from `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` if it does not exist.
- **Logout (T57, D9):** own `POST /logout` updates the user's security stamp, so every existing refresh token gets 401 at `/refresh` (F8). The client discards its tokens. The bearer access token lifetime is set to **10 minutes**; an access token issued before logout keeps working until it expires (known limit, §7).
- **Unused Identity endpoints (T55):** `MapIdentityApi` also maps `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa`, `/manage/info` (F8). Email confirmation and password reset are out of scope; the frontend does not use them and no email sender is wired for them ⚑ (whether to block them is open).
- **Isolation:** every user endpoint filters by the caller's user ID; another user's alert or destination returns 404 ⚑ (not 403, so IDs are not confirmed).

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

### 2.11 Extension points (T85, D10)

| Extension point | Backend | Frontend | Guide |
|---|---|---|---|
| Notification channel | channel interface (§2.6) + one DI registration | registry entry: channel key → destination config form | `docs/how-to/add-a-channel.md` |
| Event category | category definition: rule shape, validator, matcher, data shape + one registration | registry entry: category key → rule form and event detail view | `docs/how-to/add-a-category.md` |
| Source adapter | source interface (fetch → normalized events) + one registration and config section | none needed unless it adds a category | `docs/how-to/add-a-source.md` |

Adding a type touches only the new files and one registration per side. No `switch` or `if` on channel, category or source type outside the registries (T59, T85).

## 3. MVP scope

- Register, login, logout (own `/logout`, 10-minute access token)
- Destinations: email and Slack, with validation; webhook URLs masked
- Alert CRUD with enable/disable; per-category rules (§2.2); one or more destinations per alert
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
| Digests, per-alert digest option, throttling | T40 | Not asked; storm risk limited by T7 and T35 |
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

"Test" = automated xUnit test. "Manual" = step in the README review path.

| # | Criterion | How |
|---|---|---|
| AC1 | End-to-end: admin simulates an event → a matching alert of a normal user → email visible in Mailpit and a message in the Slack mock list | Manual + integration test ⚑ |
| AC2 | Register, login and logout work; after logout, `/refresh` with the old refresh token returns 401 | Test |
| AC3 | A normal user gets 403 on every admin endpoint; an anonymous caller gets 401 | Test |
| AC4 | A user cannot read, edit, delete or link another user's alert or destination by ID | Test |
| AC5 | Alert create, list, edit, enable/disable, delete work through the API and the UI; a disabled alert does not match | Test + manual |
| AC6 | Matching per category: disaster (magnitude, place text), market (asset, direction, threshold), news (whole-word keyword, case-insensitive) | Test (unit) |
| AC7 | An event first seen before the alert was created, or published more than 2 h before ingestion, creates no delivery | Test |
| AC8 | Market: repeated polls of the same crossing produce at most one delivery per alert and destination per day and direction; a move that grows past a higher threshold triggers that alert once | Test |
| AC9 | Each category has at least one real source that ingests events on a clean start (market: CoinGecko); simulated events are labeled | Manual (admin event list) |
| AC10 | Slack failure path: `/mock/slack/fail` (500) → 3 attempts → `Failed` with the last error; a 4xx other than 429 → `Failed` after 1 attempt | Test |
| AC11 | No `switch`/`if` on channel, category or source type outside the registries; three "how to add" guides exist | Review check |
| AC12 | Audit log: an alert change and a logout each write exactly one entry; normal user gets 403 on the audit endpoint; no webhook URL appears in any entry | Test |
| AC13 | The same event is never delivered twice to the same alert and destination (unique index; duplicate insert is rejected) | Test |
| AC14 | The alert page lists at most 3 matching events from the last 24 h for disaster and news alerts, creates no delivery rows, and is empty for market alerts | Test |
| AC15 | `docker compose up` works on a clean clone with only `.env.example` copied to `.env` | Manual (clean clone) |

## 6. Cut list (only if time runs short, in this order)

1. Styling and UI polish
2. Audit log (D12; not a brief requirement) ⚑
3. CI workflow
4. Finnhub adapter (mock stocks stay; CoinGecko stays real) ⚑
5. Delivery log filters and details (the log itself stays)
6. In-app back-fill list (D11) ⚑
7. Containerizing the apps: infrastructure stays in Compose, API and frontend start from the README
8. As a last resort, all three categories come from a simulated source instead of real feeds (still inside the brief, but logged)

**Never cut:** user management (register, login, logout), alert CRUD, all three event categories, email and Slack, the three extension points and their guides, admin view, this spec, `process/log.md` with real entries, milestone commits, prompt history, a working end-to-end path.

## 7. Known limits

- **Logout window:** an access token issued before logout stays valid for up to 10 minutes (T57).
- **Scale:** matching loops over all enabled alerts of a category per event; fine for a demo, not for large numbers of alerts (T71).
- **Region:** disaster place filter is a text match on USGS `place`, not geographic (T75); no region filter for news and market.
- **No cross-source dedup:** the same story in BBC and Guardian notifies twice (T32).
- **Market baseline:** CoinGecko's change is a rolling 24 h value; Finnhub's `dp` baseline is unverified (§8). A "move" is the provider's number, not our own calculation.
- **Feed terms of use not reviewed (T24):** notifications carry only title, link and source name, no article text.
- **Slack:** incoming webhook posts to one channel; no DMs (T63).
- **No retention policy** (T74).

## 8. Unverified facts (must be checked before the named milestone)

| Fact | Status | Check before |
|---|---|---|
| CoinGecko keyless rate limit and monthly cap | Not published (F1) | M2 (live call; fall back to mock if blocked) |
| Finnhub free per-minute limit | Unverified (F2) | M2 (real free key) |
| Finnhub `dp` baseline (previous close?) | Unverified (F9) | M2 (live call vs. `pc`) |
| Mailpit REST API paths (for an integration test) | Unverified (F11) | M3 |
| Slack incoming webhook 4xx codes | Unverified (D13) | M3 |
| Terms of use of BBC, Guardian, CoinGecko, Finnhub | Not reviewed (T24) | Accepted as a known risk |
| Identity bearer token default lifetimes and the option name for the 10-minute setting | Partly verified (F8: access token 1 h default) | M1 |
