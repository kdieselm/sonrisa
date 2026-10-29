# Brainstorm triage

- **Inputs:** [`01-raw-output.md`](01-raw-output.md) (run with planning context, possibly biased), [`02-raw-output-fresh.md`](02-raw-output-fresh.md) (fresh context), [`../brief.md`](../brief.md), [`../spec.md`](../spec.md) skeleton, [`../../process/log.md`](../../process/log.md) (D1–D7).
- **Tool / model:** Claude Code, Claude Opus 5.5 (session 02)
- **Status:** **final** (2026-10-29). The raw AI proposal is commit `fde6aa9`; the diff to this version shows what the user changed. Flagged rows and the top-5 were decided one by one (log D8, D9); then every row was reviewed with the user in batches of ten, which led to D10–D13. Rows without a decision reference were confirmed as proposed.
- **Rule:** the spec (phase 2) may use only rows whose final verdict is *accept*. *Defer* means "not in the MVP, may be named in the spec's out-of-scope list". *Verify* means "may be used only after the named check passes".

## Legend

- **Source:** 01, 02 or both.
- **Verdict:** accept / reject / defer / verify.
- **⚑** marks a row where the proposal would **override a decision already in the log or the spec skeleton**, or a row where the brainstorm made a factual or framing error. All of them were decided by the user (see "Flagged rows" below).
- **Quota rule (user decision, D8):** a source is rejected if its free quota would run out during normal testing (polling about once a minute through a working day). Sources with a large free quota stay, even with a key, as long as the key is optional.
- **F1–F9:** fact checks, recorded with source URLs in `process/log.md` (entry "Check – fact checks for the brainstorm claims", 2026-10-29 13:15).

## Fact checks (summary)

| # | Claim in the brainstorm | Result |
|---|---|---|
| F1 | CoinGecko is keyless for basic use | Verified live (keyless call returned price + 24h change). Keyless rate limit is IP-based and **not published**; Demo key: 100 calls/min. Monthly cap unverified. |
| F2 | Finnhub: key, ~60 requests/min | Key required: verified. 30 calls/s cap and 429: verified. 60/min: **unverified** on official pages. |
| F3 | Alpha Vantage: key, very low quota | Verified: 25 requests/day with a free key. |
| F4 | GDELT: keyless, ~15-minute updates | Verified for the raw files (15-minute grid, zipped CSV). DOC API keyless use: unverified. |
| F5 | Slack webhook ~1 message/sec | Verified: 1/s per webhook, bursts allowed, 429 + `Retry-After`. |
| F6 | USGS GeoJSON feeds, keyless | Verified, updated every minute; fields `id`, `mag`, `place`, `time`, `updated`, `sig`, `alert`, `title`, `url`. |
| F7 | Keyless RSS news feeds | Verified live: BBC World, Guardian World, NYT World. Terms of use for re-sending headlines: unverified. |
| F8 | No built-in logout in `MapIdentityApi` | Verified. Updating the security stamp makes `/refresh` return 401 (verified in .NET 10 source). The access token stays valid until it expires (1 hour by default). |
| F9 | Free stock APIs with more quota than Alpha Vantage | Finnhub `/quote`: free endpoint (`premium: null` in the official docs), returns `dp` (percent change), key required; free per-minute quota unverified (see F2). Twelve Data Basic: 8 requests/min, 800/day, real-time US equities (verified, pricing page). Financial Modeling Prep: pricing page returned 403, **unverified**. Massive (formerly Polygon): no free plan shown on the pricing page, **unverified**. |

## Triage table

### 1. What counts as "something important"

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T1 | Importance = any event matching the user's filter (no notion of importance) | 01-A | reject | "something **important**": matching alone notifies about everything in a topic | – |
| T2 ⚑ | One system-wide severity score 0–100 per event, user sets a minimum (spec skeleton, §1 and §2) | 01-B | **reject (decided by user, D9; overrides spec §1–§2)** | "something important" must be explainable to the user. A 0–100 score is arbitrary for news (01 itself says "hard to calibrate") and hides the native units the user thinks in (magnitude, % move). 01 presented it as the balanced middle option; this is the bias flagged in the log | Decision by user |
| T3 ⚑ | Per-category rule shapes in native units: disaster = min magnitude (+ optional place text); market = asset + min % move; news = keywords | 02-B | **accept (decided by user, D9; replaces T2)** | "users to be able to **set up alerts**": the user decides what is important to them, in units they understand; testable per category | Matching unit tests per category (spec) |
| T4 | Admin-curated importance (admin marks events important) | 01-C | reject | "**users** to be able to set up alerts"; admin would be a bottleneck, nothing is delivered without an admin | – |
| T5 | Source decides (category on/off, source "breaking"/significance flag) | 02-A | reject as the model | Thins "users set up alerts" to a toggle (02 marks it ⚠️ itself) | – |
| T6 | Global score by heuristics or an LLM | 02-C | reject | Not deterministic, not testable, new dependency and cost; nothing in the brief asks for it | – |
| T7 | System floor under user rules | 02 hybrid | accept, as a **source-level floor only** | Noise control for "important": the source config limits what is ingested (for example USGS 4.5+ feed, market moves ≥ a small minimum). No admin-tunable global floor | Config value in the spec |
| T8 | Is importance comparable across categories? | 01 | reject (question dissolved by T3) | Each category has its own rule; no cross-category comparison is needed | – |
| T9 | Escalation and revisions (USGS updates a magnitude) | both | reject re-notification | Brief is silent. The event row is updated on re-ingestion; no second notification for the same event (keeps "no double delivery") | Upsert test |
| T10 | Preview: "what would this alert have matched" | both | **accept in a limited form (user decision, D11)**; full preview deferred | Merged into T34: the alert page lists up to 3 recent matching events (disaster and news only). A free-form "last week" preview is still not asked | See T34 |
| T11 | Category list open ("that kind of thing") vs. fixed enum | both | **accept three categories in the MVP, built as an extension point (user decision, D10)** | "that kind of thing" + "flexible enough that we can add more channels later": "channels" is ambiguous (notification channels, event categories, or source APIs within a category), so all three are designed for easy extension (T85). A category is defined in one place (its rule shape and matcher), not as an enum spread through the code | Guide "How to add a category" + review check: adding one touches only the new files and one registration per side |

### 2. Where the data comes from

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T12 | Real feeds only | both (A) | reject | A demo or acceptance test needs a guaranteed trigger; real feeds may stay quiet | – |
| T13 | Simulated only | both (B) | reject | "something happens **in the world**" (02 marks it ⚠️); a keyless real source exists for every category (F1, F6, F7) | – |
| T14 | Pluggable source adapters: at least one real source per category + a simulated "inject test event" source; injected events clearly labeled | both (C) | accept | Meets "in the world" and stays demoable. Already the plan (D3); 02's labeling requirement is added. Source adapters are one of the three extension points (T85) | Admin view and notifications show "simulated"; guide "How to add a source" |
| T15 | Disasters: USGS earthquake GeoJSON | both | accept | "natural disasters"; keyless, updated every minute | F6 (verified). Feed choice (proposed `4.5_day`, polled every minute) to be fixed in the spec |
| T16 | More disaster sources: GDACS, NASA EONET, NWS | 01, 02 | defer | One real source per category meets the brief; adds formats to parse | Unverified (not checked) |
| T17 | News: RSS (one configurable feed list) | both | accept | "breaking news"; keyless | F7 (verified): BBC World as the default, Guardian World as a second feed |
| T18 | News: GDELT | 02 | defer | Verified keyless (F4), but zipped CSV of coded events, not headlines; RSS is simpler for "breaking news" | F4 |
| T19 | News: NewsAPI / GNews | 02 | reject | Needs a key (stated by 02; not checked further, since rejected anyway); conflicts with "reviewer needs no keys" (T25) | – |
| T20 ⚑ | Market sources, instead of simulated-only: **crypto** from CoinGecko (keyless, always on); **stocks** from Finnhub when `FINNHUB_API_KEY` is set in `.env`, otherwise from a **mock stock source** (generated price moves, labeled as simulated); plus the admin's simulated injector | 02 + user decision (D8) | **accept (decided by user, overrides spec §1 and D3)** | "market movements … in the world". The spec's reason for simulation ("keyless market APIs are unreliable") is contradicted by F1. Crypto trades 24/7, so a demo sees movement at any hour (T23). Stocks are covered by a real source with a large free quota (Finnhub), but the key stays optional, so the reviewer needs no sign-up (T25). CoinGecko risk: keyless limit unpublished, so one batched call every few minutes, with an optional Demo key in `.env`. If CoinGecko fails during the build, fall back to simulated and log it (cut list item 5 logic) | F1, F9. Re-check before M2: CoinGecko keyless call; Finnhub `/quote` with a real free key and its per-minute limit |
| T21 | Market: Finnhub (stocks) | both | accept as **optional** source (T20) | Large free quota (not exceeded by testing); key optional | F2 (key required, 30/s cap verified; 60/min unverified), F9 (`/quote` free, `dp` field) |
| T21a | Market: Alpha Vantage | both | **reject (user decision, D8)** | 25 requests/day (F3) runs out within the first half hour of testing | F3 |
| T21b | Market: Twelve Data | 01 | reject (quota rule, D8) | 800 requests/day and 8/min (F9): polling a few symbols every minute exceeds the daily quota | F9 |
| T21c | Market: Financial Modeling Prep, Massive/Polygon | triage research (not in the brainstorms) | reject | Free quotas could not be verified (F9); Finnhub already covers stocks | F9 (unverified) |
| T22 | A market movement is a *computed* event (needs a baseline and a window) | both | accept | "market **movements**", not prices. Decision (top-5 #2): use the provider's own change field, so no own price history is needed: CoinGecko `usd_24h_change` (rolling 24 hours), Finnhub `dp` (documented only as "Percent change"; that its baseline is the previous close is **unverified**, check with a live call). The mock produces the same shape. Both baselines must be stated in the spec | F1, F9 (fields present) |
| T23 | Equity markets are closed nights and weekends | 02 | accept (as a reason for T20) | Demo reliability | – |
| T24 | Terms of use for redistributing headlines and prices | 02 | **accept as a known risk (user decision, D9)** | Notifications contain only title, link and source name (no article text). The feed terms were not read; the spec states this as a limit | **Unverified** (terms not reviewed) |
| T25 | The reviewer needs no keys or accounts | 01 | accept | Own addition "locally verifiable" (`docs/brief.md`, own additions) | Clean-clone run (spec) |
| T26 | What happens when a feed is down | 01 | accept (minimal) | A failing source must not stop the other sources or delivery; the error is logged | Log output; admin source health is T44 |

### 3. Detection and processing

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T27 | Polling per source + match at ingestion, in-process `BackgroundService` | both (A) | accept | "get notified **when** something happens": latency = one poll interval, simplest working path. Matches the planned stack | – |
| T28 | Queue or broker (Redis, BullMQ, etc.) | both | reject | Extra infrastructure without a brief requirement; the `Deliveries` table already acts as the queue | – |
| T29 | Batch matching on a schedule with a watermark | 01-C | reject | Second timer, watermark bugs can skip or repeat events | – |
| T30 | Push or streaming ingestion | both | reject | The chosen sources offer no push | – |
| T31 | Dedup level 1: same item, same source, unique `(source, external_id)` | both | accept | "no double delivery" (CLAUDE.md risk list) starts here | Unique index + upsert test |
| T32 | Dedup level 2: same real-world event from several sources | 02 | reject for the MVP | One source per category, so no overlap; documented as a known limit | – |
| T33 | Dedup level 3: unique `(alert, event, channel)` delivery | both | accept (already in spec) | No double delivery | Unique index test |
| T34 ⚑ | Backfill policy: on first start, or for a new alert, do not notify about old events | 02 | **accept (decided by user, D9 + D11; answers spec §7 open question)** | "**when** something important happens": only new happenings are **notified**. Decision: an alert notifies only for events first seen after the alert was created; events published more than N hours before ingestion are stored but never notified. **In-app backfill (D11):** so that a brand-new account shows at first glance that matching works, the alert page lists up to **3** already-ingested matching events from the **last 24 hours**, without sending anything. Only for categories where earlier events are easy to get: disaster (USGS `4.5_day` already holds the last 24 hours) and news (RSS items carry a publish date). Not for market: CoinGecko and Finnhub give only the current change, so past crossings would need own price history | Tests: create alert, ingest old event → no delivery; new alert → list shows at most 3 matching events from the last 24 hours, and no delivery rows are created; market alert → empty list |
| T35 | Cooldown / re-arming for threshold alerts (notify once per crossing) | 02 | accept for **market only** | A 24h change above the threshold stays above it for hours; without re-arming every poll would notify. Decision (top-5 #3): a market event is one crossing (asset × direction × day); the delivery unique index then stops repeats | Matching test with consecutive polls |
| T36 | Failed send must not block other sends; retries | 02 | accept (already planned: max 3 attempts) | Reliability of "should work for both email and Slack" | Slack failure-path test (CLAUDE.md) |
| T37 | Breaking = recent: age limit, first-seen vs. published time | 01 | accept (merged into T34) | "breaking news" | – |

### 4. Real-time vs. digest

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T38 | Real-time only (bounded by polling) | both | accept | "get notified **when** something … happens" | – |
| T39 | Digest only | both | reject | Contradicts "breaking" and "when" (both runs mark it) | – |
| T40 | Per-alert digest option; throttling with overflow batching | both | defer (already out of scope in spec) | Not asked; doubles delivery logic. Storm risk is limited by T7 and T35 and documented | – |
| T41 | Quiet hours, time zones, caps per hour | both | defer | Not in the brief | – |

### 5. Admin view

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T42 | Operational view: users and their alerts, events, delivery log with status/errors/attempts, inject test event | both (A) | accept (already in spec) | "We need an admin view too" | 403 test for normal users (CLAUDE.md) |
| T43 | Admin actions: pause any alert, retry a failed delivery | 02-A | defer | Useful but not required; read-only meets the brief | – |
| T44 | Source health in admin (last poll, last error) | 02 | defer (first item to add if time allows) | Cheap and helps debug T26, but not required | – |
| T45 | Management console (users, roles, sources, keys, thresholds, channels CRUD) | both (B) | reject | Crowds out the user path; nothing in the brief asks for configuration | – |
| T46 | Editorial console | 01-C | reject | Same reason as T4 | – |
| T47 | Tenant / org admin | 02-C | reject | Needs multi-tenancy (T52) | – |
| T48 | Unauthenticated admin page or raw DB browser | 02 | reject | An unsecured admin view barely meets the brief (02 ⚠️) | 403 test |
| T49 | Can the admin see or edit other users' alerts? | both | accept read-only | The admin view needs visibility; editing on behalf of users is not asked and raises privacy questions | 403 + read-only endpoints |
| T50 | Audit log | 02 | **accept (user decision, D12)** | Not required by the brief, but it supports "admin view": the admin can see who changed what. Scope: append-only table of time, acting user, action, target type and ID, outcome. Actions: register, login (success and failure), logout, alert and destination create/update/delete/enable/disable, admin "simulate event". No secrets in entries (webhook URLs masked, no passwords or tokens). Read-only list in the admin view. Written in one place (a service called from the endpoints), not scattered through the code. The spec decides its position in the cut list | Tests: an alert change and a logout each write one entry; a normal user gets 403 on the audit endpoint; no webhook URL appears in an entry |
| T51 | How the first admin is created | both | accept (already planned): seeded from config | Needed for "admin view" on a clean clone | Clean-clone run |

### 6. Users and roles

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T52 | Organisations / teams owning alerts and Slack destinations | both (B) | reject | "users" are individuals in the brief; multiplies schema and authorization | – |
| T53 | Self-registration, two roles (User, Admin) | both (A) | accept (already decided, D2) | "**users** to be able to set up alerts" | Register/login/logout tests |
| T54 | Stubbed identity (user picker) | both (C) | reject (already decided, D2) | Weakens "users to be able to…" | – |
| T55 | Email verification, password reset, SSO, magic link | both | defer (already out of scope) | Not in the brief. Note: `MapIdentityApi` also maps `/confirmEmail`, `/forgotPassword`, `/resetPassword` (F8); the spec must say they are unused (no email sender for them) | F8 |
| T56 ⚑ | Product context: standalone app or a feature inside an existing product | 02 (top-5 #3) | **accept standalone; not a top-5 decision (decided by user, D9)** | The brief names no existing product, stack or user base. 02's argument rests on "a small product feature", a phrase from **our own brainstorm prompt**, not the brief (log entry 13:17) | grep of `docs/brief.md` |
| T57 ⚑ | Logout | (D5, not in brainstorms) | **accept own `/logout` that updates the security stamp, plus a short access token (decided by user, D9)** | Real user management (D2) | F8: `/refresh` returns 401 after a stamp change (verified in source). **New gap:** the access token stays valid for up to 1 hour by default. Decision: `BearerTokenExpiration` about 10 minutes, and the spec states the remaining window. Immediate revocation (stamp check on every request) rejected as extra work without a brief requirement |

### 7. Channels and extensibility

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T58 | Code-level channel interface + registry (`validate`, `render`, `send`); destinations stored generically as `{type, config JSON}`; matching produces channel-neutral notifications | both (A) | accept (already planned) | "flexible enough that we can add more channels later" | Code structure + "how to add a channel" guide |
| T59 | Anti-pattern: `if email … else if slack` in core code, or `email` / `slack_url` columns on the alert table | both | reject; becomes a review check | Fails "add more channels later" | Diff review: no channel-specific branch outside channel classes |
| T60 | Generic outbound webhook as an extra channel | 02-B | defer | A good extensibility demo, but a third channel is not asked; the guide covers it | – |
| T61 | Runtime plugin system; channels added by admins at runtime | both | reject (confirmed by user, D10) | "**we** can add more channels later" reads as the team (developers), not admins; overengineered. The admin does not add types or configure them at runtime | – |
| T62 | Outsourced notification service | 01-C | reject | Hides the design the brief asks for; needs an external account | – |
| T63 | Slack: incoming webhook vs. OAuth app (bot token, DMs) | 02 | accept incoming webhook | OAuth needs a public redirect URL and app setup, which breaks "locally verifiable". Webhook posts to one channel; DMs out. Locally, a mock receiver in the API (`POST /mock/slack/{name}`) logs and stores each message for the admin view; a new Slack destination defaults to the mock URL; a real webhook URL can be set per destination or for the seeded demo user via `.env` | – |
| T64 | Slack errors: rate limit (429 + `Retry-After`) and other failures | 02 | **accept (user decision, D13)**: 429 and 5xx (and timeouts) are retryable, respecting `Retry-After`; any other 4xx (invalid or deleted webhook) is a final failure at once | A broken webhook does not improve with retries; the retry path stays testable through the mock | F5 (verified). Slack's exact 4xx error codes: **unverified**, check before M3. Test: mock `/mock/slack/fail` returns 500 → 3 attempts → `Failed` |
| T65 | Email: dev catcher (Mailpit) vs. real inbox | 02 | accept Mailpit locally; SMTP host configurable via env for a real server; **sent with MailKit (user decision, D13)** | "Should work for both email": real SMTP is sent; Mailpit is only the receiver. Microsoft advises against `SmtpClient` for new development and points to MailKit (F10) | F10, F11; MailKit 4.18.2 on NuGet (published 2026-10-23, targets `net10.0`). Email visible in Mailpit (spec) |
| T66 | Where the email address comes from | 01 | accept: email destination, defaulting to the account email | Generic destinations (T58) | – |
| T67 | One alert → several channels; several destinations per channel | both | accept | "both email and Slack" per alert; generic destinations make several per channel free | – |
| T68 | Per-channel formatting (HTML email vs. Slack text) | both | accept | Each channel renders its own message | – |
| T69 | Validate destination config; "send test message" | both | accept validation, defer test message | Validation prevents broken destinations; test-send is extra UI | – |
| T70 | Slack webhook URL is a secret | 02 | accept: never logged, masked in API responses | A webhook URL lets anyone post to the channel | Review check; encryption at rest deferred |

### 8. Scale

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T71 | Demo scale: loop over active alerts per new event, documented limit | both (A) | accept | Brief gives no scale; "the limit must be written down" (01) | Stated in spec |
| T72 | Design for growth now (indexed matching, sharding, streaming) | both (B, C) | reject | Premature; costs the end-to-end path | – |
| T73 | "Seams" only where retries and dedup need them anyway (delivery table, idempotent worker) | 01-C | accept | Same structure as T31–T36, no speculative abstraction | – |
| T74 | Retention of events and delivery history | 02 | defer | Not in the brief; documented as a known limit | – |

### 9. Other ambiguities

| # | Point | Source | Verdict | Reason (brief phrase) | Verification |
|---|---|---|---|---|---|
| T75 | Region filtering | both | accept for disasters only, as text match on USGS `place`; reject for news and market | "in the world": region is natural for disasters, unclear for the others. Text match is not geographic; stated as a limit | F6 (`place` field exists) |
| T76 | Notification content: title, category, key value (magnitude / % move), link, source, why it matched | both | accept | "get notified": the user must see what happened and why | Email and Slack sample in tests or screenshots |
| T77 | Unsubscribe / one-click disable from the message | both | defer; message links to the alert page in the app | One-click needs signed tokens; compliance note in spec | – |
| T78 | Disclaimers: "not an official warning", "not financial advice", data delay | 02 | accept (one line per template) | Cheap, avoids misleading users about "natural disasters" and "market movements" | Template review |
| T79 | Failure surfacing to users; auto-pause on a broken destination | both | defer | Admin sees failures (T42); user-facing status is extra | – |
| T80 | Alert lifecycle: create, edit, enable/disable, delete | both | accept (already in spec) | "set up alerts" | UI + API |
| T81 | User-facing notification history | 02 | defer | Not asked; admin delivery log covers debugging | – |
| T82 | Concrete acceptance demo scenario | 02 | accept (already in spec §5) | The brief gives no acceptance criteria; the spec must | Spec §5 |
| T83 | Admin changes global thresholds, alerts change silently | 02 | reject (dissolved) | No admin-tunable thresholds (T7) | – |
| T84 | Language and geography of sources | 02 | accept: English-language feeds, global | "in the world"; stated as an assumption | – |
| T85 ⚑ | Three extension points, not one: **notification channels**, **event categories**, **source adapters within a category**. Each has a backend interface + one registration, and a frontend registry (for example a map from type to form component) so a new type needs no change in shared code on either side. No admin configuration, no runtime plugins | user decision (D10) | **accept (user decision, D10)** | "add more channels later" is ambiguous; covering all three readings avoids betting on one. Extension is code-level (developers), backend **and** frontend | Three short guides ("How to add a channel / category / source"); review check: no `switch` on type outside the registries |

## Flagged rows (decided by the user)

| Row | What it overrides or corrects | Decision |
|---|---|---|
| T2 / T3 | Spec §1 and §2: severity score 0–100 with a user threshold | **Accepted (D9):** per-category rules in native units; no 0–100 score |
| T20 | Spec §1 and D3: market source is simulated because "keyless market APIs are unreliable" | **Decided (D8):** CoinGecko crypto (keyless) + Finnhub stocks with optional key, mock stocks without a key, + simulated injector. Alpha Vantage and other low-quota APIs rejected |
| T34 | Spec §7 open question (back-fill or not) | **Accepted (D9, refined by D11):** no back-fill **notifications**; only events first seen after the alert's creation, with an age limit. In-app list of up to 3 matching events from the last 24 hours (disaster and news only), nothing sent |
| T24 | Was "verify" (feed terms not read) | **Accepted as a known risk (D9):** title, link and source name only; stated as a limit in the spec |
| T85 | T11 (only notification channels extensible, chosen silently) | **Accepted (D10):** three code-level extension points (channel, category, source), backend and frontend; no admin configuration |
| T56 | 02's top-5 list (product context), based on our prompt's wording | **Accepted (D9):** standalone; dropped from the top-5 |
| T57 | D5 assumption that logout "invalidates" the session | **Accepted (D9):** refresh tokens invalidated (verified); access token about 10 minutes, remaining window documented |

## Top-5 decisions (accepted by the user, D9)

Merged from both runs' lists. 02's "product context" (T56) is dropped; the admin scope follows from the others (02's note) and is already settled (T42).

1. **Importance and rule model** (T2, T3, T7): per-category rules in native units (magnitude, % move, keywords), plus a source-level floor. Drives the alert schema, the matching code and the user UI.
2. **Sources per category** (T14, T15, T17, T20, T22): USGS `4.5_day`, BBC World RSS, CoinGecko 24h change for a few crypto assets, Finnhub stocks with an optional key (mock stocks without one), plus a labeled simulated injector. Drives the event model and demo reliability.
3. **Delivery semantics** (T27, T31, T33, T34, T35, T38): real-time via polling, dedup at item and delivery level, no back-fill, market re-arming per crossing. Drives the worker and the unique indexes.
4. **Extension points, channel contract and destinations** (T58, T63, T65, T67, T70, T85): one interface with validate/render/send, generic destinations with JSONB config, Slack incoming webhook, SMTP to Mailpit. Categories and source adapters get the same treatment (interface + registration, frontend registry). Proves "add more channels later" under every reading.
5. **Auth and roles** (T53, T57, T42/T49): self-registration, User/Admin, seeded admin, own `/logout` with security stamp and an access token of about 10 minutes, read-only admin view.
