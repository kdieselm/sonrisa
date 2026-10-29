# Plan of attack

The task asks me to define my own deliverables, their order and the reasons. This is that plan. Changes to it are logged in [`process/log.md`](../process/log.md).

## Principles behind the order

1. **Understand before deciding.** The brief is deliberately vague, so the first outputs are questions and interpretations, not solutions.
2. **Validate AI output before building on it.** Every AI-generated planning document passes a separate validation step (fact checks, accept/reject decisions) before the next phase may use it.
3. **Documents before code.** The spec and architecture fix the decisions; the code implements them.
4. **Vertical slices, each manually verifiable.** Implementation runs in thin end-to-end slices (backend + frontend + tests). After each slice I check the result in the browser before the commit. Auth and the data model are still first, because everything depends on them. (Replaced "the UI comes last", see D17 in the log.)
5. **Visible judgment.** Raw AI output and the revised version are committed separately, so the diff shows what was changed.

## Phases and deliverables

| # | Phase | Deliverable | Why here | Commit(s) |
|---|---|---|---|---|
| 0 | Setup | Repo skeleton, verbatim brief, `CLAUDE.md` rules, spec skeleton with requirements traceability, process log, prompt templates | The rules and the source of truth must exist before any AI work | `chore: repo skeleton and planning docs` |
| 1 | Brainstorm | Raw questions and competing interpretations ([`docs/brainstorm/`](brainstorm/)), two runs: one with planning context, one fresh | Surfaces what the brief leaves open; the fresh run checks for bias | `docs: raw brainstorm output` |
| 1b | **Brainstorm validation (triage)** | [`docs/brainstorm/03-triage.md`](brainstorm/03-triage.md): every point of both runs with a verdict (accept / reject / defer / verify), a reason tied to the brief, and fact checks against official sources | The brainstorm is AI output and may contain bias and hallucinated facts. Nothing reaches the spec without an explicit decision | `docs: raw triage proposal` → `docs: brainstorm triage decisions` |
| 2 | Spec | [`docs/spec.md`](spec.md): assumptions, MVP scope, out of scope, acceptance criteria. Built **only** from accepted triage items | Fixes what is built and how it is checked | `docs: raw AI spec draft` → `docs: revise spec after review` |
| 3 | Architecture | [`docs/architecture.md`](architecture.md): one diagram and 3–5 short ADRs | Fixes how it is built; decisions with alternatives | `docs: raw architecture draft` → `docs: architecture and ADRs` |
| Z | Wrap-up | README with reviewer path, retrospective, exported prompts | Makes the process reviewable | `docs: readme, retrospective, prompts` |

## Implementation slices

Each slice is backend + frontend + its own tests, and ends with a manual check in the browser (D17). AC numbers refer to [`docs/spec.md`](spec.md) §5.

| # | Slice | Content | Manual check | ACs | Commit |
|---|---|---|---|---|---|
| S1 | Foundation and auth | Docker Compose (Postgres, API, frontend), EF Core migrations at startup, Identity register/login/refresh, own `/logout`, 10-minute access token; Angular landing, register, login, logout pages; integration test base (Testcontainers PostgreSQL); CI (build + test) | `docker compose up` → register, log in, log out in the browser | AC2 | `feat: foundation and auth` |
| S2 | Admin | `Admin` role, seeded admin, role claim in the bearer token, admin area: users, audit log (register, login, logout) | Admin logs in, sees the new user and the audit entries; a normal user cannot open the admin area | AC3, AC12 (auth part) | `feat: admin area and audit log` |
| S3a | Events | Category interface and registry (disaster, news: data shape, simulate fields), source adapter interface and registry, USGS, Guardian RSS with BBC as fallback (D26), simulated injector; admin event list and "simulate event" form | Real events appear in the admin list within 10 minutes; a simulated event carries the "simulated" label | AC9 (USGS, RSS, label), AC16 (event part), AC17 (sources) | `feat: event ingestion` |
| S3b | Alerts and notifications page | Destinations (email, Slack config, masking), alert CRUD (disaster, news), matching, delivery rows, back-fill list, **Notifications** page (status `Pending`, nothing is sent yet) | Create an alert → simulate a matching event → it appears on the Notifications page | AC4, AC5, AC6, AC7, AC12 (alert part), AC13, AC14, AC16 (delivery part), AC18, AC21 | `feat: alerts and notifications page` |
| S4 | Delivery | Delivery worker, MailKit → Mailpit, Slack webhook and mock receiver, retry; admin delivery log and Slack mock list; Notifications page shows `Sent` / `Failed` | Email in Mailpit, message in the Slack mock list, a `/fail` destination ends `Failed` after 3 attempts | AC1, AC9 ("SIMULATED" in the message), AC10, AC17 (worker), AC19, AC20 | `feat: email and Slack delivery` |
| S5 | Market | CoinGecko, mock stocks, optional Finnhub, market rule with re-arm and steps | A market alert notifies at the documented points of the §2.4 example (simulated moves) | AC8, AC9 (CoinGecko parsing) | `feat: market movements` |
| S6 | Hardening | Final pass over the three "how to add" guides (written from S3a on, D24), the AC11 `grep` check, clean-clone run | `docker compose up` on a clean clone with only `.env.example` copied | AC11, AC15 | `docs: extension guides` |

## How every slice runs

1. 5–10 line plan → my approval
2. Implementation, including the slice's automated tests
3. Review of the diff with [`prompts/templates/review.md`](../prompts/templates/review.md)
4. Log entries for findings and decisions
5. Manual check steps listed by the AI → I run them in the browser
6. Commit after my approval

## If time runs short

Only the cut list in [`docs/spec.md`](spec.md) applies, in its order. Every cut is logged.
