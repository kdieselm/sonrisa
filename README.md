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

1. **Planning** (claude.ai): a first plan and a handoff file. I dropped the full BMAD method (D1), and rejected two places where the AI scoped the brief down: seeded users instead of real accounts (D2), and two event sources instead of all three categories (D3).
2. **Setup and brainstorm:** the handoff was checked line by line against the original task; it had turned "locally verifiable" into a requirement, which the task does not contain, so it is marked as my own addition in [docs/brief.md](docs/brief.md). The brainstorm ran twice, in the planning context and in a fresh subagent, to see how much that context biased it.
3. **Triage:** every brainstorm point was accepted, changed or rejected in a table, with fact checks (F1–F11) against official sources and live data. Nothing reached the spec without passing the triage.
4. **Spec and architecture:** requirements traceability, assumptions, acceptance criteria, a cut list, ADRs. Both were committed raw, attacked by a fresh subagent with [prompts/templates/challenge.md](prompts/templates/challenge.md), and committed again revised (`bf14840` → `2fbd5d3`, `bc48dae` → `849cebb`).
5. **Implementation in vertical slices S1–S6** (D17): each slice was planned in plan mode, approved by me (four plan proposals first went back with my objections or questions), built with its tests, reviewed and committed after my approval.
6. **Wrap-up:** exported transcripts, the late fresh reviews of S5 and S6 and their fixes, this section.

### How the AI was used

- Roughly one Claude Code session per phase or slice ([prompts/sessions/](prompts/sessions/)), driven by [CLAUDE.md](CLAUDE.md): plan before code, no unapproved packages, log decisions when they happen, commit only after my approval.
- **Fresh subagents as reviewers**, with no history of the work: the second brainstorm, the spec and architecture challenges, and the diff reviews of S1–S4 before their commits ([prompts/templates/review.md](prompts/templates/review.md)); S5 and S6 only after theirs (see below).
- The AI wrote the code and the tests. I approved or rejected the plans, decided the open questions, and checked the running app.

### How the output was checked

- **Facts against sources:** package existence and maintenance, framework behaviour (the role claim in the bearer token, read in the framework source), feed URLs and formats against live responses, recorded as test fixtures. AI summaries of documentation were checked too: one misreported a USGS field that the live feed has.
- **Tests that can fail:** from S2 on, after a slice's tests passed, the code was broken on purpose (mutation check) to see them fail. Each of the seven generated migrations was read; one (`DeliveryRowVersion`) tried to add the PostgreSQL system column `xmin` and was emptied by hand.
- **Real infrastructure:** integration tests on PostgreSQL with Testcontainers (two bugs showed only there: time precision, and an EF Core query that could not be translated), CI on GitHub Actions (green on the last pushed commit, `7c8a098`), and [scripts/clean-clone-check.sh](scripts/clean-clone-check.sh), which runs the whole stack from the committed state with only `.env.example`.
- **My checks of the running app:** confirmed in chat for S1, S2 and S3b; in S3a and S4 what I saw led to changes (too much BBC news → D26; a wrong text on the account page). For S5 and S6 the transcripts show only my approval of the commit, so I checked both in the browser again in phase Z, after the review fixes.

### The most important corrections

| What the AI did | What was wrong | What changed |
|---|---|---|
| Planned seeded users and a "login as" dropdown | Weakened "users to be able to set up alerts" | Real register, login, logout (D2) |
| Presented the handoff's choices as the balanced middle option in the brainstorm | Bias from the planning context, visible only against the fresh run | Triage against the brief, not the handoff |
| Took my prompt's "a small product feature" for a phrase of the brief | Prompt wording leaks into the output | Product context decided from the brief alone (T56) |
| Proposed to commit the triage with ~80 unflagged rows "approved" because I had not objected | Silence is not approval; one of them (T11) read "channels" as notification channels only | Every row reviewed in batches of ten; three extension points (D10) |
| One market event per asset, direction and UTC day | Self-contradictory: an alert created after the day's first move stayed silent all day, and AC8 could not pass | Re-arm with hysteresis and optional steps (D14) |
| First phase 3 plan built everything at once | Not checkable step by step | Vertical slices, each checked by me (D17) |
| S2 admin seeding added an existing account to `Admin` | A stranger who registered the seed email first would become admin (fresh review) | Seeding never promotes an existing account |
| Compared event times at full .NET precision | PostgreSQL keeps microseconds: every poll would have rewritten every event; only the integration test saw it | Times rounded at the entity |
| S4 delivery counted the attempt after the send | A failed save after a send repeated the send without limit (fresh review) | Attempt saved before the send |
| S5 and S6 committed without a fresh review | The late review found two market rule bugs and an AC11 test blind to branches on key constants | Fixed and mutation-checked (`48fb8f7`) |

### What did not go to plan

- S5 was committed after a self-review only, S6 without a diff review; the fresh reviews ran in phase Z and found bugs that tests and the self-review had missed.
- The log times up to phase Z were estimated by the AI, not read from the clock, and many entries were written in batches at the end of a slice. The order is right, the clock times are not (marked at the top of the log).
- Session 01 started the triage session twice; one copy was left after one answer (both are exported).

Details of every decision, check and finding: [process/log.md](process/log.md). Every prompt: [prompts/](prompts/).

## Documents

| What | Where |
|---|---|
| Original task (verbatim) | [docs/brief.md](docs/brief.md) |
| Specification and requirements traceability | [docs/spec.md](docs/spec.md) |
| Architecture and ADRs | [docs/architecture.md](docs/architecture.md) |
| Raw brainstorm output | [docs/brainstorm/](docs/brainstorm/) |
| Decision and AI-review log | [process/log.md](process/log.md) |
| Prompts and templates | [prompts/](prompts/) |
| Session transcripts | [prompts/sessions/](prompts/sessions/) |
| Rules for the AI agent | [CLAUDE.md](CLAUDE.md) |
