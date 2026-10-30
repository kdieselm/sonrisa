# World Event Alerts

Users set up alerts and get notified by email or Slack when something important happens in the world: breaking news, market movements, natural disasters. Channels are pluggable, and admins get an overview of users, alerts, events and deliveries.

This repository is a take-home task: build a feature from a deliberately vague brief using AI agents. **The process is the main deliverable.** The plans, decision log, prompts and intermediate outputs show how the AI was directed and how its output was checked; the code is the evidence.

## How to run

### Requirements

| Needed for | Requirement |
|---|---|
| Running the app (required) | **Docker** with Compose v2: [Docker Desktop](https://www.docker.com/products/docker-desktop/) on Windows / macOS, or Docker Engine + the Compose plugin on Linux. Docker must be **running** before `docker compose up`. Nothing else is needed: the API, the frontend, PostgreSQL and Mailpit all run in containers. |
| Running the automated tests (optional) | .NET 10 SDK, and Docker running: the integration tests start their own PostgreSQL container with Testcontainers. |
| Running the frontend outside Docker (optional) | Node.js 24 and npm. |

No API keys or accounts are needed.

### Start

```bash
cp .env.example .env
docker compose up --build
```

Then open http://localhost:8080 (the port is `WEB_PORT` in `.env`). Mailpit's web UI is on http://localhost:8025.

### Tests

```bash
dotnet test --solution src/backend/Sonrisa.slnx
```

## Seeded accounts

From `.env.example` (local development only; change them in `.env`):

| Account | Email | Password |
|---|---|---|
| Admin | `admin@example.local` | `change-me-Admin1!` |
| Demo user (email + Slack destination, one disaster alert) | `demo@example.local` | `change-me-Demo1!` |

Any other account is made with **Register** in the app and is a normal user.

## Five-minute reviewer path (AC1)

1. `cp .env.example .env`, then `docker compose up --build`, and open http://localhost:8080.
2. Log in as the demo user. **Alerts** → new alert: category *Breaking news*, keyword `reviewer`, both destinations.
3. In a private window, log in as the admin → **Simulate event** → *Breaking news*, title `Reviewer test`. (Or use *Market movement* / *Natural disaster* with a matching alert.)
4. As the demo user, **Notifications** shows the message once per destination, `Pending` then `Sent`.
5. Mailpit (http://localhost:8025) holds exactly one email titled `Reviewer test`; the admin **Slack mock** list holds the Slack message. Both say **SIMULATED**. (The demo user also has a disaster alert for magnitude 5 and above, so a real earthquake can add other emails to the same inbox.)
6. The admin area also shows events (real USGS, Guardian/BBC, CoinGecko and mock-stock events appear within minutes), the delivery log and the audit log.

## Checks

| Check | Command | Expected |
|---|---|---|
| Tests (needs Docker for PostgreSQL via Testcontainers) | `dotnet test --solution src/backend/Sonrisa.slnx` | all green |
| Clean clone (AC15): clones the **committed** state, copies only `.env.example`, runs the end-to-end path of AC1 on its own ports (so the default ports are not part of this check), tears down; needs bash (Git Bash on Windows) | `bash scripts/clean-clone-check.sh` | `CLEAN-CLONE CHECK: PASSED` |
| No `switch`/`if` on channel, category or source keys outside the registries (AC11) | see below | only the ten `const string …Key = "…"` definition lines |

AC11 by hand, in bash (Git Bash on Windows; the brace expansion does not work in PowerShell). The grep finds key *literals* only; `RegistryBoundaryTests` also finds comparisons, `case` and switch arms on the key *constants* (`SlackChannel.ChannelKey`), takes the key list from the real registrations (so config keys such as `world-news` count), and checks the frontend `.ts` and `.html` files:

```bash
grep -rnE '"(disaster|news|market|email|slack|usgs|world-news|coingecko|finnhub|mock-stocks|simulated)"' src/backend --include=*.cs --exclude-dir={obj,bin,Migrations,Sonrisa.Tests}
```

A key literal anywhere else is a violation. Each "how to add" guide ([channel](docs/how-to/add-a-channel.md), [category](docs/how-to/add-a-category.md), [source](docs/how-to/add-a-source.md)) lists the files a new type adds and the one registration line it changes; `GuidePathTests` fails if a guide names a file, folder or test that does not exist.

## How I worked

TODO: phases, how the AI was used, the most important corrections.

## Documents

| What | Where |
|---|---|
| Original task (verbatim) | [docs/brief.md](docs/brief.md) |
| Specification and requirements traceability | [docs/spec.md](docs/spec.md) |
| Architecture and ADRs | [docs/architecture.md](docs/architecture.md) |
| Raw brainstorm output | [docs/brainstorm/](docs/brainstorm/) |
| Decision and AI-review log | [process/log.md](process/log.md) |
| Screenshots | [process/screenshots/](process/screenshots/) |
| Prompts and templates | [prompts/](prompts/) |
| Session transcripts | [prompts/sessions/](prompts/sessions/) |
| Rules for the AI agent | [CLAUDE.md](CLAUDE.md) |
