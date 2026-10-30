# Prompts

All prompts of the project. Full transcripts, with every user message verbatim, are in [`sessions/`](sessions/). This page lists, per phase, the opening prompt and the user inputs that changed the direction of the work. The quotes are translated from Hungarian; the originals are in the transcripts.

## Per phase

| Phase | Session | Opening prompt | User inputs that changed the work |
|---|---|---|---|
| Planning | 00 (claude.ai) | TODO: export | Produced the planning handoff; D1 (no full BMAD), D2 (real register/login, not seeded users), D3 (all three event categories) |
| 0. Setup, 1. Brainstorm | [01](sessions/01-setup-and-brainstorm.md) | "Based on the task-04 description I made a preliminary plan with Claude … The original task description must be kept fully in mind and cannot be changed. The handoff is a guide … but the planning phase should be detailed and well documented." Attachments: `planning-handoff.md`, the task `.docx` | Will install Docker; no BMAD at all, plan here only; `docs/brief.md` = the task verbatim; own `/logout` endpoint. Rejected an edit: "don't write that I asked for 6 hours … no scope-narrowing decision". Brainstorm prompt: [`brainstorm-opening.md`](brainstorm-opening.md), run twice (in session 01 and in a fresh subagent) |
| 1b. Triage | [02](sessions/02-triage.md), [02b](sessions/02b-triage-parallel.md) | Brief written by session 01 for the new session | Make the system flexible for any meaning of "channel" (categories, sources, notification channels); back-fill so a new account shows data at once; audit log; asked for the Mailpit and Slack approach to be explained first. In 02b: drop Alpha Vantage and any source whose free quota testing would exceed |
| 2. Spec | [03](sessions/03-spec.md) | "Next phase; everything is in the earlier commits, and here is a handover doc" | Market alerts must not be capped at one notification per day; frequent polling, with a brainstorm of options → D14; answers to four review questions → D14–D16. Review: [`templates/challenge.md`](templates/challenge.md) in a fresh subagent, plus triage-contradiction, flagged-value and AC-testability checks |
| 3. Architecture | [04](sessions/04-architecture-and-slices.md) | "Let's plan phase 3." | **Rejected the first plan:** "don't implement the whole project at once … split it into smaller slices, and after each one I can check the work by hand" (suggested order: DB + register/login + landing page; admin; notifications listed in the UI; Slack and email) → D17. "Notifications" = what was sent by email or Slack, under a Notifications menu item → D18. Backend split into more projects (DTO, config, entity, controller, service …) instead of four |
| S1 | [05](sessions/05-s1-foundation-and-auth.md) | "Let's plan S1." | Docker as a README requirement for testing the task |
| S2 | [06](sessions/06-s2-admin.md) | "Let's plan S2." | "Don't return full lists from the DB"; user directory and the other lists paged |
| S3a | [07](sessions/07-s3a-events.md) | "Let's plan S3a." | **Rejected the plan** with questions: what "MaxAge 2 h" means; how "place required" decides importance (a user location and a distance?); the docs must say how to add new categories and channels later → the "how to add" guides (D24). Then: BBC out by default (too much news); Guardian first, retried, with a circuit breaker that falls back to BBC (D26) |
| S3b | [08](sessions/08-s3b-alerts.md) | "Let's plan S3b." | **Rejected the plan:** market movements had dropped out of the events; the category is required, simulated with random data if no free key is available |
| S4 | [09](sessions/09-s4-delivery.md) | "Let's plan S4." | Account page text said alerts appear there, but they are on separate tabs → fixed. "A fresh reviewer for the S4 diff" → subagent review, fixes in a separate commit (`fdbb8b0`) |
| S5, S6 | [10](sessions/10-s5-and-s6.md), [10a](sessions/10a-s6-planning-first-start.md) | "Plan S6 now, then back to S5, and update S6 with what S5 adds. Better finished and incomplete than all three categories in but not running." | Plan feedback: the repository is published, CI can run; CI result reported by the user before the S6 commit |
| Z. Wrap-up | [11](sessions/11-wrap-up.md) | "Let's go through the items of phase Z one by one." | Full transcripts rather than only the user's prompts |

Every slice from S1 on followed the same loop: "Let's plan Sx" → plan in plan mode → approval or rejection with reasons → implementation with tests → review → the user's manual check in the browser → "commit". The review step differed: S1–S4 were reviewed by a fresh subagent with [`templates/review.md`](templates/review.md) (S4 also by the writing session first); S5 had only a self-review by the session that wrote it; S6 had no diff review, its checks were the clean-clone run, mutation checks of the new tests and the guide dry run (see `process/log.md`).

## Templates

- [`templates/task.md`](templates/task.md) – start a piece of work; plan before code
- [`templates/review.md`](templates/review.md) – review a diff; problems only
- [`templates/challenge.md`](templates/challenge.md) – attack a plan, spec or schema
