# Specification

> Status: **skeleton.** The traceability table is filled in; every other section is a starting point. `TODO: finalize after brainstorm`.

## 1. Requirements traceability

Every row maps a phrase of the brief ([`brief.md`](brief.md)) to how it is built and how it is checked. No row may be dropped or weakened.

| Brief | Requirement | Implementation | Verification |
|---|---|---|---|
| "users to be able to…" | User management | Register, login, logout (own `/logout` endpoint that invalidates refresh tokens); every alert belongs to one user; users only see their own alerts | Two users cannot see each other's alerts, not even by ID (test + manual check); after logout the refresh call returns 401 (test) |
| "set up alerts" | Alert management | Create, list, edit, delete, enable/disable | UI + API |
| "get notified when something important happens" | Event processing, importance | Normalized `Event` with a severity score (0–100); the alert sets the threshold | Matching unit tests |
| "in the world — like breaking news, market movements, natural disasters" | All three categories | Disasters: USGS feed. News: RSS. Market: simulated source (keyless market APIs are unreliable; ADR) | Admin page shows events per category |
| "Should work for both email and Slack" | Two channels | SMTP email (Mailpit), Slack incoming webhook (mock or real) | Email in Mailpit; message in the mock's log |
| "flexible enough that we can add more channels later" | Extensibility | `INotificationChannel` interface, per-channel config (JSONB), DI registration, "How to add a channel" guide | Code structure + guide |
| "We need an admin view too" | Admin interface | `Admin` role, seeded admin account; users and their alerts, events, delivery log, simulate event | Normal user gets 403 on the admin API (test), and the UI guard blocks the page |

## 2. Assumptions

TODO: finalize after brainstorm.

- **Event model:** source, category (News / Market / Disaster), title, description, URL, time, region (optional), severity (0–100), external ID for deduplication.
- **"Important":** severity normalized per source. Earthquakes: from magnitude. RSS: base value plus keywords. Simulated: set by hand. "Important" is therefore a configurable threshold per alert.
- **Alert rule:** categories + keywords (optional) + minimum severity + region (optional) + one or more channels.
- **Sources (`IEventSource`):** USGS (real), RSS (real), simulated (triggered from the admin page). Feed URLs must be verified before implementation.
- **Delivery:** matching event → `Delivery` record (alert × event × channel, unique index against double delivery) → background worker → channel.

## 3. MVP scope

TODO: finalize after brainstorm.

- Registration, login, logout
- Alert CRUD with enable/disable
- Event ingestion from USGS, RSS and a simulated market source
- Matching and delivery by email and Slack, with retry (max 3 attempts)
- Admin: users and their alerts, event list, delivery log (status, error, attempts), "Simulate event" form
- `docker compose up` runs everything locally

## 4. Out of scope

TODO: finalize after brainstorm. Each item needs a reason.

- Digests / scheduled summaries
- ML-based importance scoring
- External OAuth sign-in
- Email address confirmation, password reset
- Rate limiting
- Additional channels (only the abstraction and the guide)

## 5. Acceptance criteria

TODO: finalize after brainstorm.

- End-to-end path: simulated event → matching alert → email in Mailpit (and Slack message in the mock log)
- A normal user gets 403 on every admin endpoint
- A user cannot read, edit or delete another user's alert by ID
- The same event is never delivered twice to the same alert and channel
- A failing Slack webhook is retried and ends in a final failure status after 3 attempts
- `docker compose up` works on a clean clone

## 6. Cut list (only if time runs short, in this order)

1. Styling and UI polish
2. CI workflow
3. Delivery log filters and details (the log itself stays)
4. Containerizing the apps: infrastructure stays in Compose, API and frontend start from the README
5. As a last resort, all three categories come from a simulated source instead of real feeds (still inside the brief, but logged)

**Never cut:** user management (register/login), alert CRUD, all three event categories, email and Slack, channel abstraction, admin view, this spec, `process/log.md` with at least 5–8 real entries, milestone commits, prompt history, a working end-to-end path.

## 7. Open questions

TODO: answer after brainstorm.

- What triggers ingestion? Proposal: a `BackgroundService` polls the real sources every minute; matching runs at ingestion; the simulated source runs on an admin button.
- Which RSS feed(s)? Must be a real, stable, keyless feed; verify before M2.
- How is RSS severity derived (base value, keyword list)?
- Is region matching meaningful for news and market events, or only for disasters?
- What happens to events that arrive before an alert is created (no back-fill, or back-fill)?
