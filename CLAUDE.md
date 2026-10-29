# CLAUDE.md

## Purpose

Take a deliberately vague product brief (user alerts for world events, delivered by email and Slack, extensible channels, admin view) to a working implementation using AI agents. **The process is evaluated, not the code:** how the work is framed, which decisions are made and why, where the AI is corrected, and how its output is verified.

Source of truth: [`docs/brief.md`](docs/brief.md) (the original task, verbatim).

## Rules (mandatory for the whole project)

1. **Every requirement in docs/brief.md is mandatory. If a proposal would drop or weaken any of them, flag it and stop.** Only what the brief leaves open may be simplified, and only with a written reason. Example: "users to be able to…" means real user management (register, login, logout); seeded users are not enough.
2. **Plan first, code second.** Every milestone starts with a 5–10 line plan. No code until the plan is approved.
3. **Visible judgment.** Significant AI-generated documents are committed raw first, then the revised version in a separate commit. The diff is the evidence.
4. **No invented dependencies.** A new NuGet or npm package needs approval and must be a real, maintained package. Verify that every package and API exists before using it.
5. **Log as you go.** Decisions and AI-review findings go into [`process/log.md`](process/log.md) when they happen, not at the end. Use the entry format at the top of that file.
6. **Scope cuts** only from the cut list in `docs/spec.md`, in that order, and each cut is logged.
7. **Repo language is English.** Conversation with the user may be in Hungarian.
8. **Commits** at each milestone, with meaningful messages. Commit only after the user approves.

## Tech stack and conventions

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core Web API, .NET 10 (LTS) |
| ORM | EF Core with migrations, applied automatically at startup |
| Database | PostgreSQL in Docker Compose (JSONB for per-channel config) |
| Auth | ASP.NET Core Identity API endpoints (`AddIdentityApiEndpoints` + `MapIdentityApi`) with `AddRoles<IdentityRole>()`; own `/logout` endpoint that invalidates refresh tokens |
| Background work | `BackgroundService` + `Deliveries` table with status, max 3 attempts |
| Frontend | Angular (latest stable), standalone components |
| Email (local) | Mailpit container (SMTP + web UI) |
| Slack | Incoming webhook; local mock receiver endpoint in the API that logs the message; real webhook URL optional via `.env` |
| Tests | xUnit (matching logic, authorization, logout) |
| CI | GitHub Actions: build + test |
| Run | `docker compose up` (Postgres, Mailpit, API, frontend) |

Layout: `src/backend/` (Api, Contracts, Application, Domain, Infrastructure, Tests; one project per layer, controllers for own endpoints, see `docs/architecture.md` §2), `src/frontend/` (Angular app).

## Working mode

- Phases, order and deliverables: [`docs/plan.md`](docs/plan.md).
- **Validate before building on AI output.** Brainstorm results reach the spec only through the triage (`docs/brainstorm/03-triage.md`). Facts (APIs, packages, limits, URLs, framework behavior) are checked against official sources; anything unverified is marked as such and never presented as fact.
- Use the templates in [`prompts/templates/`](prompts/templates/): `task.md` to start work, `review.md` for diff review, `challenge.md` to attack a plan or schema.
- After each milestone: run a review of the diff, log the findings, then propose a commit.
- Risky points to verify (each one gets a log entry): package/API existence, role claim in the bearer token (normal user → 403 on admin API), user isolation by ID, matching correctness, no double delivery (unique index), Slack failure path (500 → retry → final failure), generated migration content, real feed URLs and formats, `docker compose up` on a clean clone.

## Key documents

- [`docs/plan.md`](docs/plan.md) – plan of attack: phases, deliverables, order
- [`docs/spec.md`](docs/spec.md) – requirements traceability, assumptions, scope, acceptance criteria
- [`docs/architecture.md`](docs/architecture.md) – diagram and ADRs
- [`process/log.md`](process/log.md) – decision and AI-review log
