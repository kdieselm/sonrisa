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

---

## Phase 1 – brainstorm (Claude Code session 01)

### [2026-10-29 12:25] Brainstorm run in a context that already knew the plan
- Type: check
- What the AI proposed / generated: Raw brainstorm output for `prompts/brainstorm-opening.md`, saved unedited to [`docs/brainstorm/01-raw-output.md`](../docs/brainstorm/01-raw-output.md).
- What was wrong with it / what I weighed: The run happened in the same session that had already read the planning handoff. The "competing interpretations" may therefore lean toward the decisions already made (severity score, hybrid sources, polling, strategy interface, operational admin view). A fresh session without the handoff would give a less biased baseline.
- What I did instead: Kept the output raw and marked the limitation in its header. In phase 2 the spec is challenged with `prompts/templates/challenge.md`, and each interpretation that matches the handoff must be justified against the brief, not against the handoff.
- How I verified: Review of the raw output against the brief in phase 2 (next entries).

### [2026-10-29 12:32] Fresh-context brainstorm vs. the first run
- Type: ai-review
- What the AI proposed / generated: A second run of the same prompt in a subagent with no history, no handoff and no repo access: [`docs/brainstorm/02-raw-output-fresh.md`](../docs/brainstorm/02-raw-output-fresh.md).
- What was wrong with it / what I weighed: Compared the two outputs topic by topic. The bias suspected in the previous entry is visible: in the first run, the interpretation that matches the handoff is always presented as the balanced middle option (severity 0–100 + user threshold, hybrid sources with simulated market, polling, strategy interface, operational admin). The fresh run does not converge on these. Findings that the first run missed or played down:
  1. **Importance model.** The fresh run proposes *per-category rule shapes* (news: keywords; market: symbol + % move in a window; disaster: type, region, minimum severity) as the main user-driven option, and treats a single global score as one option among several. The handoff's 0–100 severity is not the only reasonable reading.
  2. **Market data.** It names a keyless real option (CoinGecko, crypto) and points out that equity markets are closed nights and weekends. This challenges the handoff's claim that market data must be simulated because keyless market APIs are unreliable. Also: a market movement is a *computed* event (needs a baseline and a window, so state).
  3. **Deduplication at three levels** (same item, same real-world event from several sources, same notification), plus a **backfill policy** on first start and **cooldown / re-arming** for threshold alerts. The first run only covered the first and third.
  4. **Product context** (standalone app or a feature inside an existing product) as a top-5 decision.
  5. **Safety and compliance:** disclaimers (disaster alerts are not official warnings; "not financial advice"), unsubscribe links, Slack webhooks stored as secrets.
  6. **Slack:** webhook vs. OAuth app, channel vs. DM, about 1 message/second per webhook.
  7. **Anti-pattern to avoid:** `email` / `slack_url` columns on the alert table instead of generic destinations.
- What I did instead: Both outputs stay raw. Phase 2 (spec) must explicitly decide items 1–7 above; the handoff's choices are inputs, not defaults. Item 2 can overturn an existing decision (simulated market source) and needs a verified answer.
- How I verified: Side-by-side reading of both files. The factual claims of the fresh run (CoinGecko keyless access, Finnhub and Alpha Vantage limits, GDELT update interval, Slack webhook rate limit) are **unverified** and must be checked against official docs before they are used in the spec.

### [2026-10-29 13:05] D7 – Separate validation phase for the brainstorm
- Type: course-correction
- What the AI proposed / generated: After the brainstorm, the AI planned to go straight to phase 2 and decide the open points while writing the spec draft.
- What was wrong with it / what I weighed: I asked when we decide what stays from the brainstorm. Folding validation into the spec hides exactly the judgment the task evaluates: which AI points were accepted, rejected or deferred, and which facts were checked. It also lets unverified facts slip into the spec.
- What I did instead: New phase 1b, brainstorm triage: one table of all points from both runs, each with a verdict, a reason tied to the brief, and fact checks against official sources; raw proposal and final decisions as separate commits. The spec may use only accepted items. The plan of attack was also missing from the repo (it only existed in the session's plan file), so it is now [`docs/plan.md`](../docs/plan.md).
- How I verified: `docs/plan.md` and `CLAUDE.md` updated; the triage runs in a new session with a fresh context.

---

## Phase 1b – brainstorm triage (Claude Code session 02)

### [2026-10-29 13:15] Check – fact checks for the brainstorm claims
- Type: check
- What the AI proposed / generated: Both brainstorm runs state facts about sources, limits and the framework (02 marks them unverified itself).
- What was wrong with it / what I weighed: None of them may reach the spec unchecked. Each claim was checked on the provider's own page; where possible, also with a live request from this machine (`curl`, 2026-10-29).
- What I did instead: Results (F-numbers are referenced in [`docs/brainstorm/03-triage.md`](../docs/brainstorm/03-triage.md)):

  | # | Claim | Result | Source |
  |---|---|---|---|
  | F1 | CoinGecko works without a key | **Verified (live), limit unverified.** Keyless `GET /api/v3/simple/price?ids=bitcoin&vs_currencies=usd&include_24hr_change=true` returned 200 with price and `usd_24h_change`. Docs: public API is "IP-based rate limiting — shared across all users on the same IP", no number published; free Demo key: 100 calls/min. Monthly cap: **unverified**. | https://docs.coingecko.com/docs/common-errors-rate-limit |
  | F2 | Finnhub: key, ~60 requests/min | **Key required: verified.** 429 when exceeded and a 30 calls/second cap on top of every plan: verified. "60/min on the free plan": **unverified** on official pages (only a GitHub issue in the official `finnhub-js` repo and third-party sites). | https://finnhub.io/docs/api/rate-limit |
  | F3 | Alpha Vantage: key, very low daily quota | **Verified:** free key, 25 requests/day. | https://www.alphavantage.co/support/ |
  | F4 | GDELT: free, no key, ~15-minute updates | **Verified:** "update every 15 minutes"; live `lastupdate.txt` lists files stamped `20261029121500` (15-minute grid), downloaded without a key. Format: zipped CSV. Keyless use of the DOC 2.0 API: **unverified**. | https://blog.gdeltproject.org/gdelt-2-0-our-global-world-in-realtime/, http://data.gdeltproject.org/gdeltv2/lastupdate.txt |
  | F5 | Slack webhook: ~1 message/sec | **Verified:** 1 message per second per incoming webhook, short bursts allowed; HTTP 429 with `Retry-After` when exceeded. (`api.slack.com/docs/rate-limits` now redirects to `docs.slack.dev`.) | https://docs.slack.dev/apis/web-api/rate-limits |
  | F6 | USGS earthquake GeoJSON feeds, keyless | **Verified:** summary feeds `.../summary/{significant,4.5,2.5,1.0,all}_{hour,day,week,month}.geojson`, "Updated every minute". Live `4.5_day.geojson`: 200, 28 features. Feature `id`; `properties` include `mag`, `place`, `time`, `updated`, `sig`, `alert`, `tsunami`, `title`, `url`. | https://earthquake.usgs.gov/earthquakes/feed/v1.0/geojson.php |
  | F7 | Keyless RSS news feeds exist | **Verified (live):** BBC World `https://feeds.bbci.co.uk/news/world/rss.xml` (200, RSS 2.0), Guardian World `https://www.theguardian.com/world/rss` (200), NYT World `https://rss.nytimes.com/services/xml/rss/nyt/World.xml` (200). Terms of use for re-sending headlines: **unverified**. | live requests |
  | F8 | `MapIdentityApi` has no logout (D5) | **Verified.** .NET 10 docs list the endpoints (`/register`, `/login`, `/refresh`, `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa`, `/manage/info`); no logout. The docs' own `/logout` sample calls `SignOutAsync`, which "does **not** invalidate stateless Bearer tokens". The `/refresh` handler in the .NET 10 source rejects the call with 401 when `ValidateSecurityStampAsync` fails, so D5's plan (update the security stamp on logout) works for refresh tokens. **Gap found:** the access token stays valid until it expires (1 hour by default). | https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0, https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs |
- How I verified: Official pages via web fetch, plus live HTTP requests. Remaining unverified items are marked as such in the triage and must not appear as facts in the spec.

### [2026-10-29 13:16] AI-review – the page-reading tool misreported a USGS field
- Type: ai-review
- What the AI proposed / generated: The web-fetch summary of the USGS feed documentation said that `title` is not in `properties` (only in `metadata`).
- What was wrong with it / what I weighed: A live feed contradicts it: every feature has `properties.title` (for example "M 4.9 - …").
- What I did instead: Used the live payload as the source of truth for field names (F6). Summaries of documentation pages are themselves AI output and get checked against the real data when it matters.
- How I verified: Parsed `4.5_day.geojson` and listed the property keys.

### [2026-10-29 13:17] AI-review – "a small product feature" comes from my prompt, not the brief
- Type: ai-review
- What the AI proposed / generated: `02-raw-output-fresh.md`, topic 9: "'a small product feature' suggests an existing product", used to argue for "product context" as a top-5 decision.
- What was wrong with it / what I weighed: The phrase is not in [`docs/brief.md`](../docs/brief.md). During triage the AI first logged this as an invented quote. A second search showed the source: my own [`prompts/brainstorm-opening.md`](../prompts/brainstorm-opening.md) opens with "We are starting the design of a small product feature". So the run did not hallucinate; it took my prompt's framing as if it were the brief. Two lessons: the prompt wording leaks into the output, and a "hallucination" claim also needs checking before it is logged.
- What I did instead: Triage row T56 decides product context from the brief alone (no existing product, stack or users are named, so the app is standalone). The prompt's framing is not treated as a requirement.
- How I verified: `grep` for the phrase across `docs/` and `prompts/`: found only in the prompt and in the 02 output.

### [2026-10-29 13:40] D8 – Market sources: no low-quota APIs; optional Finnhub, mock stocks
- Type: decision
- What the AI proposed / generated: Triage row T20/T21 (raw proposal): CoinGecko for crypto plus the simulated injector; Finnhub and Alpha Vantage both rejected because they need a key.
- What was wrong with it / what I weighed: Rejecting every keyed API leaves stocks without a real source. The real constraint is quota, not the key: Alpha Vantage's 25 requests/day (F3) runs out within the first half hour of testing; a source with a large free quota is fine if its key is optional. I asked whether any free stock API allows more. Checked: Finnhub `/quote` is a free endpoint with a percent-change field (`dp`) (F9); Twelve Data allows 8/min and 800/day (F9), too little for polling every minute; FMP and Massive/Polygon could not be verified.
- What I did instead: Rule: a source is rejected if its free quota would run out during normal testing. Alpha Vantage and Twelve Data out. Stocks: Finnhub when `FINNHUB_API_KEY` is set, otherwise a mock stock source (labeled simulated); crypto: CoinGecko keyless. The reviewer still needs no key. Applied to `03-triage.md` before the raw-proposal commit at my request, so the raw commit already contains this decision; this entry is the record of the change.
- How I verified: F9 (official Finnhub docs JSON for `/quote`: `premium: null`, `dp` = "Percent change"; Twelve Data pricing page). Finnhub's free per-minute limit is still unverified; to be checked with a real free key before M2.
