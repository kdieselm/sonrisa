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

TODO: seeded admin account, 5-minute reviewer path.

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
| Retrospective | `process/retrospective.md` (TODO) |
| Screenshots | [process/screenshots/](process/screenshots/) |
| Prompts and templates | [prompts/](prompts/) |
| Session transcripts | [prompts/sessions/](prompts/sessions/) |
| Rules for the AI agent | [CLAUDE.md](CLAUDE.md) |
