# Plan of attack

The task asks me to define my own deliverables, their order and the reasons. This is that plan. Changes to it are logged in [`process/log.md`](../process/log.md).

## Principles behind the order

1. **Understand before deciding.** The brief is deliberately vague, so the first outputs are questions and interpretations, not solutions.
2. **Validate AI output before building on it.** Every AI-generated planning document passes a separate validation step (fact checks, accept/reject decisions) before the next phase may use it.
3. **Documents before code.** The spec and architecture fix the decisions; the code implements them.
4. **Vertical risk first.** Auth and the data model come first because everything depends on them; the UI comes last because it only consumes the API.
5. **Visible judgment.** Raw AI output and the revised version are committed separately, so the diff shows what was changed.

## Phases and deliverables

| # | Phase | Deliverable | Why here | Commit(s) |
|---|---|---|---|---|
| 0 | Setup | Repo skeleton, verbatim brief, `CLAUDE.md` rules, spec skeleton with requirements traceability, process log, prompt templates | The rules and the source of truth must exist before any AI work | `chore: repo skeleton and planning docs` |
| 1 | Brainstorm | Raw questions and competing interpretations ([`docs/brainstorm/`](brainstorm/)), two runs: one with planning context, one fresh | Surfaces what the brief leaves open; the fresh run checks for bias | `docs: raw brainstorm output` |
| 1b | **Brainstorm validation (triage)** | [`docs/brainstorm/03-triage.md`](brainstorm/03-triage.md): every point of both runs with a verdict (accept / reject / defer / verify), a reason tied to the brief, and fact checks against official sources | The brainstorm is AI output and may contain bias and hallucinated facts. Nothing reaches the spec without an explicit decision | `docs: raw triage proposal` → `docs: brainstorm triage decisions` |
| 2 | Spec | [`docs/spec.md`](spec.md): assumptions, MVP scope, out of scope, acceptance criteria. Built **only** from accepted triage items | Fixes what is built and how it is checked | `docs: raw AI spec draft` → `docs: revise spec after review` |
| 3 | Architecture | [`docs/architecture.md`](architecture.md): one diagram and 3–5 short ADRs | Fixes how it is built; decisions with alternatives | `docs: raw architecture draft` → `docs: architecture and ADRs` |
| M1 | Skeleton and auth | .NET solution, EF Core + Postgres, Docker Compose, Identity with roles, seeded admin, `/logout` | Everything else depends on users and the database | `feat: skeleton and auth` |
| M2 | Events and alerts | Event model, real and simulated sources, alert CRUD, matching | The core domain | `feat: event ingestion and alerts` |
| M3 | Delivery | Channel abstraction, email and Slack, delivery records, retry | Completes the end-to-end path | `feat: notification delivery` |
| M4 | Frontend | Angular: register/login/logout, own alerts, admin view | Consumes a finished API | `feat: frontend` |
| T | Tests | Matching, authorization and logout tests; clean-clone run | Proves the acceptance criteria | `test: …` |
| Z | Wrap-up | README with reviewer path, retrospective, exported prompts | Makes the process reviewable | `docs: readme, retrospective, prompts` |

## How every milestone runs

1. 5–10 line plan → my approval
2. Implementation
3. Review of the diff with [`prompts/templates/review.md`](../prompts/templates/review.md)
4. Log entries for findings and decisions
5. Commit after my approval

## If time runs short

Only the cut list in [`docs/spec.md`](spec.md) applies, in its order. Every cut is logged.
