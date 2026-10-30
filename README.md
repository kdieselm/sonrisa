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

### The plan of attack

I treated the brief as a requirements problem first and a coding problem second. The work ran in phases, each with its own deliverable and commit ([docs/plan.md](docs/plan.md)):

1. **Planning** (claude.ai): a first plan and a handoff file. The AI's first proposal scoped the brief down too far (seeded users, only two event sources); I rejected that (D1–D3).
2. **Setup and brainstorm:** the handoff was checked line by line against the original task; the brainstorm ran twice, once in the planning context and once in a fresh subagent, to see how much the context biased it.
3. **Triage:** every brainstorm point was accepted, changed or rejected in a table, with fact checks against official sources. Nothing reached the spec without passing the triage.
4. **Spec and architecture:** requirements traceability, assumptions, acceptance criteria, a cut list, ADRs. Each was committed raw, attacked by a fresh subagent with [prompts/templates/challenge.md](prompts/templates/challenge.md), and committed again revised.
5. **Implementation in vertical slices S1–S6** (D17): each slice was planned in 5–10 lines, approved, built with its tests, reviewed, checked by hand in the browser, and committed.
6. **Wrap-up:** exported transcripts, late reviews, this section.

### How the AI was used

- One Claude Code session per phase or slice, driven by [CLAUDE.md](CLAUDE.md): plan before code, no unapproved packages, log decisions when they happen, commit only after my approval.
- **Fresh subagents as reviewers**, with no history of the work: the second brainstorm, the spec and architecture challenges, and the diff review of every slice ([prompts/templates/review.md](prompts/templates/review.md)).
- The AI wrote the code and the tests; I approved or rejected plans, decided the open questions, and ran the manual checks.

### How the output was checked

- **Facts against sources:** package existence and maintenance, framework behaviour (role claim in the token, Identity endpoints), feed URLs and formats against live responses, recorded as fixtures.
- **Tests that can fail:** after each slice's tests passed, the code was broken on purpose (mutation check) to see the tests fail. Every generated migration was read.
- **Real infrastructure:** integration tests on PostgreSQL with Testcontainers; CI on GitHub Actions; a clean-clone script that runs the whole stack from the committed state.
- **Manual check** in the browser at the end of every slice.

### The most important corrections

| What the AI did | What was wrong | What changed |
|---|---|---|
| Planned seeded users and a "login as" dropdown | Weakened "users to be able to set up alerts" | Real register, login, logout (D2) |
| Presented the handoff's choices as the balanced middle option in the brainstorm | Bias from the planning context, visible only against a fresh run | Fresh-context run, triage against the brief |
| Treated ~80 unflagged triage rows as approved because I had not objected | Silence is not approval; T11 hid a wrong reading of "channels" | Every row reviewed; three extension points (D10) |
| First phase 3 plan built everything at once | Not checkable step by step | Vertical slices with a manual check each (D17) |
| A test checked an in-memory fake | PostgreSQL keeps microseconds, .NET 100 ns: every poll would have rewritten every event | Times rounded at the entity; the integration test caught it |
| Market alerts capped at one per day | Contradicted the brief's market movements | Re-arm with hysteresis and steps (D14) |
| S4 delivery counted the attempt after the send | A failed save after a send repeated the send without limit | Write-ahead attempt (fresh review) |
| S5 and S6 committed without a fresh review | The late review found two market rule bugs and an AC11 test that could not see branches on key constants | Fixed with mutation checks (`48fb8f7`) |

### What did not go to plan

- S5 and S6 were committed after a self-review only; the fresh reviews ran in phase Z and found real bugs.
- The log times up to phase Z were estimated by the AI, not read from the clock, and several entries were written in batches. The order is right, the times are not (marked in the log).
- Session 01 started the triage session twice; one copy was left after one answer.

Details of every decision, check and finding: [process/log.md](process/log.md). Every prompt: [prompts/](prompts/).

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
