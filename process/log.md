# Process log

Decisions, AI-review findings, course-corrections and checks, written **when they happen**.

## Entry format

```
### [YYYY-MM-DD HH:MM] <short title>
- Type: decision | ai-review | course-correction | check
- What the AI proposed / generated:
- What was wrong with it / what I weighed:
- What I did instead:
- How I verified:
```

Sessions referenced below are listed in [`prompts/sessions/README.md`](../prompts/sessions/README.md).

---

## Planning (claude.ai conversation, before the repo existed)

### [2026-10-29 --:--] D1 – No full BMAD Method
- Type: course-correction
- What the AI proposed / generated: Run the full BMAD Method (Analyst → PM → Architect → SM → Dev → QA).
- What was wrong with it / what I weighed: The task evaluates how *I* structure the work, not how well I follow a ready-made checklist. Full BMAD produces far more documentation than an MVP needs.
- What I did instead: BMAD only for brainstorming; all other planning in my own lightweight documents. (Later narrowed further, see D4.)
- How I verified: Compared against the task text: "we want to see how you scope and structure work, not just how you execute a predefined checklist."

### [2026-10-29 --:--] D2 – Real registration and login instead of seeded users
- Type: course-correction
- What the AI proposed / generated: A scoped-down plan with seeded users, a "login as" dropdown and header-based user identification.
- What was wrong with it / what I weighed: The brief says "users to be able to set up alerts". That requires user management; the shortcut would have weakened a brief requirement.
- What I did instead: Real registration and login with ASP.NET Core Identity.
- How I verified: Checked each brief phrase against the plan (became the traceability table in `docs/spec.md`).

### [2026-10-29 --:--] D3 – All three event categories covered
- Type: course-correction
- What the AI proposed / generated: A scoped-down plan with only a simulated source and USGS; RSS and market data cut.
- What was wrong with it / what I weighed: The brief names all three examples (breaking news, market movements, natural disasters). Cutting two would have departed from it.
- What I did instead: USGS (real), RSS (real), market (simulated, with an ADR explaining why).
- How I verified: Traceability table row "in the world — …" in `docs/spec.md`.

---

## Phase 0 – repo setup (Claude Code session 01)

### [2026-10-29 12:10] Check – planning handoff vs. the original task text
- Type: ai-review
- What the AI proposed / generated: The claude.ai planning handoff (summary of the planning conversation) lists the submission requirements, including "must be locally verifiable".
- What was wrong with it / what I weighed: Compared line by line with the original task (docx). "Locally verifiable" is **not** in the original task; the earlier AI conversation added it and presented it as a requirement. Everything else matched.
- What I did instead: Kept it as my own deliverable, but marked it in `docs/brief.md` under "Own additions (not in the original task)".
- How I verified: Extracted the docx text and compared it with the handoff's "submission requirements" list.

### [2026-10-29 12:12] D4 – No BMAD at all
- Type: decision
- What the AI proposed / generated: Install BMAD (`npx bmad-method install`) and run the Analyst brainstorm in this repo.
- What was wrong with it / what I weighed: Adds an npm dependency and a framework for a single step. A plain brainstorm prompt shows the same thing (questions and competing interpretations) and is easier to review.
- What I did instead: Brainstorm in Claude Code with my own prompt: [`prompts/brainstorm-opening.md`](../prompts/brainstorm-opening.md). Output goes raw into `docs/brainstorm/`.
- How I verified: n/a (scope decision).

### [2026-10-29 12:13] D5 – Own `/logout` endpoint
- Type: decision
- What the AI proposed / generated: The plan listed logout as part of user management, with auth via `MapIdentityApi`.
- What was wrong with it / what I weighed: `MapIdentityApi` provides register/login/refresh but no logout endpoint. Options: client-side token discard only, or a server endpoint that invalidates refresh tokens.
- What I did instead: Own `/logout` endpoint (updates the security stamp, so existing refresh tokens stop working) plus client-side token discard. Planned for M1.
- How I verified: To be verified in M1 with a test: after logout, `/refresh` returns 401. The claim "no built-in logout" must also be checked against the .NET 10 docs before M1.

### [2026-10-29 12:14] D6 – `docs/brief.md` holds the full task text
- Type: decision
- What the AI proposed / generated: The handoff planned `docs/brief.md` as the brief plus a short list of submission requirements.
- What was wrong with it / what I weighed: A summary can drift from the source (see the "locally verifiable" finding above). The agent should always work from the original wording.
- What I did instead: The full task description, verbatim; own additions in a separate, labeled section.
- How I verified: Word-by-word comparison of `docs/brief.md` with the text extracted from the docx (see next entry).

### [2026-10-29 12:16] Check – development environment
- Type: check
- What the AI proposed / generated: The plan assumes Docker Compose for Postgres, Mailpit and the apps.
- What was wrong with it / what I weighed: Checked the machine: .NET SDK 10.0.401, Node 24, git are installed; **Docker is not installed**, and neither is the `gh` CLI.
- What I did instead: I will install Docker Desktop before M1. Phase 0 does not need it. The GitHub repo will be created by hand.
- How I verified: `dotnet --list-sdks`, `node --version`, `docker --version`, `git --version`.
