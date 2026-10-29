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

### [2026-10-29 14:00] D9 – Triage decisions on the flagged rows and the top-5
- Type: decision
- What the AI proposed / generated: Raw triage proposal (commit `fde6aa9`): flagged rows T2/T3, T34, T56, T57, one open "verify" row (T24) and a top-5 list.
- What was wrong with it / what I weighed: Each flagged row either overrides the spec skeleton or the log, or rests on an unchecked premise. Decided one by one:
  - **T2/T3 (overrides spec §1–§2):** the 0–100 severity score is dropped. It was the planning handoff's choice, and the first brainstorm run presented it as the balanced middle (the bias noted in phase 1). A score for news would be arbitrary. Alerts use per-category rules in native units: disaster = minimum magnitude + optional place text; market = asset + minimum % move; news = keywords.
  - **T34 (answers spec §7):** no back-fill. An alert matches only events first seen after it was created; events published more than N hours before ingestion are never notified.
  - **T56:** standalone app; "product context" is not a top-5 decision (its argument came from our own prompt, see 13:17).
  - **T57 (refines D5):** `/logout` updates the security stamp (refresh token → 401, F8); the access token lifetime goes down to about 10 minutes, and the spec states the remaining window. Per-request stamp validation rejected: extra work, no brief requirement.
  - **T24:** feed terms of use not reviewed; accepted as a known risk (notifications carry only title, link and source name).
  - **Top-5:** accepted as proposed, with the D8 market sources.
- What I did instead: Applied to [`docs/brainstorm/03-triage.md`](../docs/brainstorm/03-triage.md), now final. Rows not discussed one by one keep the proposed verdict.
- How I verified: Diff `fde6aa9..` on `03-triage.md` shows exactly these changes.

### [2026-10-29 14:01] Triage – rejected and still-unverified points (for the spec)
- Type: ai-review
- What the AI proposed / generated: The two brainstorm runs (84 triage rows).
- What was wrong with it / what I weighed: Points that do not reach the spec and facts that are still open.
- What I did instead:
  - **Rejected** (reason in the triage row): T1 filter-only importance, T2 0–100 score, T4 admin-curated importance, T5 source-decides model, T6 LLM/heuristic score, T8, T9 re-notify on revisions, T12 real-only, T13 simulated-only, T19 NewsAPI/GNews, T21a Alpha Vantage, T21b Twelve Data, T21c FMP/Massive, T28 broker, T29 batch matching, T30 push ingestion, T32 cross-source dedup, T39 digest-only, T45 management console, T46 editorial console, T47 tenant admin, T48 unauthenticated admin, T50 audit log, T52 teams, T54 stubbed identity, T59 channel-specific columns/branches, T61 runtime plugins, T62 outsourced notifications, T72 scale-out design, T83 global thresholds.
  - **Still unverified, must not appear as fact in the spec:** CoinGecko keyless rate limit and monthly cap (F1); Finnhub free per-minute limit (F2) and the baseline of `dp` (F9); GDELT DOC API keyless use (F4, deferred anyway); FMP and Massive free quotas (F9); RSS/CoinGecko/Finnhub terms of use (T24); GDACS/EONET/NWS (T16, not checked, deferred).
- How I verified: Cross-checked against the verdict column of `03-triage.md`.

### [2026-10-29 14:20] D10 – Three extension points: channels, categories, sources
- Type: decision
- What the AI proposed / generated: Triage T11 (raw): fixed three categories, "only **channels** must be extensible", with channels read as notification channels; T58 channel interface; T61 runtime plugins rejected.
- What was wrong with it / what I weighed: "add more channels later" is ambiguous: it can mean notification channels, event categories, or source APIs inside a category. The AI picked one reading without saying it was a choice. When asked what "extensible by the admin" should mean, I first put it that way myself, then corrected it: the admin does not need to add types; the system must be designed so that a new type is easy to integrate on the backend and the frontend.
- What I did instead: New triage row T85: three code-level extension points (notification channel, event category, source adapter), each with a backend interface + one registration and a frontend registry, so a new type needs no change in shared code. T11 and T14 updated; T61 (runtime plugins, admin-added types) stays rejected. Three short "how to add" guides become deliverables.
- How I verified: Phase 2 acceptance criterion to add: a review check that no `switch`/`if` on channel, category or source type exists outside the registries.

### [2026-10-29 14:35] D11 – Limited in-app backfill for new alerts
- Type: decision
- What the AI proposed / generated: T34 (D9): no back-fill at all; a new alert only reacts to events first seen after its creation.
- What was wrong with it / what I weighed: On a brand-new account nothing is visible until the world produces a matching event, so a reviewer cannot tell at first glance whether matching works. Asked for a small back-fill, only where earlier events are easy to get, with a limit. The AI checked per category: disaster (USGS `4.5_day` already holds the last 24 hours) and news (RSS items have a publish date) are easy; market is not (CoinGecko and Finnhub return only the current change, so past threshold crossings would need own price history). Two options: send the back-filled events as marked notifications, or only show them in the app.
- What I did instead: In-app only. The alert page lists up to 3 already-ingested matching events from the last 24 hours, for disaster and news alerts; nothing is sent and no delivery rows are created. Notifications keep the D9 rule. T10 (preview) is accepted in this limited form.
- How I verified: To be covered by tests in M2 (list limit and window, no delivery rows, empty for market).

### [2026-10-29 14:50] D12 – Audit log is in scope
- Type: decision
- What the AI proposed / generated: Triage T50: reject the audit log ("not in the brief").
- What was wrong with it / what I weighed: The brief does not require it, but an admin view without a record of who changed what is thin, and it is cheap if written in one place. It also makes the auth tests (login, logout) visible in the UI.
- What I did instead: Accepted. Append-only audit table (time, acting user, action, target type and ID, outcome) for register, login success and failure, logout, alert and destination changes, and the admin "simulate event"; no secrets in entries; read-only list in the admin view. The AI proposed this scope; the spec places it in the cut list.
- How I verified: Tests planned for M1/M2: one entry per alert change and per logout; 403 for normal users on the audit endpoint; no webhook URL in any entry.

### [2026-10-29 15:05] Check – email and Slack mechanics (F10, F11)
- Type: check
- What the AI proposed / generated: Triage T63–T65: Slack incoming webhook with a local mock receiver, SMTP to Mailpit.
- What was wrong with it / what I weighed: Before explaining the design, checked the two facts it rests on.
- What I did instead:
  - **F10 – SMTP client in .NET 10:** the official `SmtpClient` docs say "We don't recommend that you use the `SmtpClient` class for new development because `SmtpClient` doesn't support many modern protocols. Use MailKit or other libraries instead." It still works for plain SMTP to Mailpit. Choice between the built-in class (no new package) and MailKit (new NuGet package, needs approval under rule 4) is open. Source: https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient?view=net-10.0
  - **F11 – Mailpit:** official image `axllent/mailpit`, SMTP on 1025, web UI on 8025 by default; a REST API (v1) exists, its exact paths are **unverified**. Source: https://mailpit.axllent.org/docs/install/docker/
- How I verified: Official documentation pages, read on 2026-10-29.

### [2026-10-29 15:15] D13 – MailKit for SMTP; Slack error policy
- Type: decision
- What the AI proposed / generated: Explanation of the email and Slack mechanics (triage T63–T65): delivery rows, worker, registry; Mailpit as SMTP catcher; Slack incoming webhook with a local mock receiver that stores messages; two open choices.
- What was wrong with it / what I weighed: (1) The built-in `SmtpClient` needs no package, but Microsoft advises against it for new development (F10). (2) Retrying every Slack error wastes attempts on a broken webhook.
- What I did instead: **MailKit approved** as a new NuGet package (rule 4). Slack: 429, 5xx and timeouts are retried (respecting `Retry-After`); any other 4xx fails at once. The mock's `/mock/slack/fail` returns 500 so the retry path stays testable.
- How I verified: MailKit exists and is maintained: NuGet registration shows 4.18.2, published 2026-10-23 by Jeffrey Stedfast, with a `net10.0` target (https://www.nuget.org/packages/MailKit). Slack's exact 4xx codes are unverified; check before M3.

### [2026-10-29 15:25] Course-correction – every triage row reviewed, not only the flagged ones
- Type: course-correction
- What the AI proposed / generated: After the flagged rows and the top-5 were decided (D9), the AI proposed to commit, with the note that the other rows "keep the proposed verdict: the user reviewed the raw table and raised no objection".
- What was wrong with it / what I weighed: That note treated silence as approval for about 80 rows. Unflagged rows can still carry a wrong interpretation; T11 did (it silently read "channels" as notification channels only).
- What I did instead: Went through all rows in batches of ten. Changes: D10 (three extension points), D11 (limited in-app backfill), D12 (audit log), D13 (MailKit, Slack error policy). The status line of `03-triage.md` now says so.
- How I verified: Each batch was confirmed explicitly in the conversation; the diff from `fde6aa9` shows every change.

---

## Phase 2 – spec (Claude Code session 03)

### [2026-10-29 15:45] Raw AI spec draft
- Type: ai-review
- What the AI proposed / generated: A full rewrite of [`docs/spec.md`](../docs/spec.md) from the final triage (accept rows only, each statement citing its T-row or D-decision). Phase plan approved first: raw draft → challenge review in a fresh subagent + coverage check → revision.
- What was wrong with it / what I weighed: Not yet reviewed. The draft marks with ⚑ every value or rule the triage left open (one category per alert, market direction, keyword semantics, 1 % market floor, poll intervals, assets, 2 h age limit, market re-matching per crossing per UTC day, retry delays, 404 vs 403 for other users' IDs, unused Identity endpoints, cut-list positions of audit log / Finnhub / in-app back-fill). It also deviates from T33: the delivery key is `(alert, event, destination)`, not `(alert, event, channel)`, because T67 allows several destinations per channel.
- What I did instead: Committed raw so the review diff shows every change.
- How I verified: Next entries (challenge review, coverage check).

### [2026-10-29 16:00] AI-review – challenge review of the raw spec (fresh subagent) + coverage check
- Type: ai-review
- What the AI proposed / generated: Raw spec `bf14840`, reviewed with [`prompts/templates/challenge.md`](../prompts/templates/challenge.md) by a subagent with no session history (plus checks for triage contradictions, ⚑ consistency and AC testability). Separately, a script cross-checked T-row citations in the spec against the triage verdicts.
- What was wrong with it / what I weighed: Main findings (verified by me where they are factual):
  1. **Market model is self-contradictory** (§2.3, §2.5, AC8): one event per asset × direction × UTC day + "first seen after the alert was created" + 2 h age limit means an alert created after the day's first ≥ 1 % move stays silent all day, and AC8's second clause cannot pass. The UTC day also splits a rolling 24 h move at midnight.
  2. **News importance reduces to a topic filter** for common keywords (two feeds, no cross-source dedup, no throttling): close to the T1 reading that was rejected.
  3. **Hooks into `MapIdentityApi` endpoints are assumed:** "role `User` on register" and audit entries for register/login need code inside framework endpoints; the spec does not say how.
  4. **Cut item 8 ("all categories simulated, still inside the brief") contradicts T13** ("in the world"). Verified: the wording comes from the original spec skeleton (`b61bb94`), i.e. it was mine, not this draft's.
  5. Real Slack is never exercised; every AC uses the mock.
  6. ACs not testable as written: AC8 passes with zero deliveries, AC9 depends on the market, AC11 has no procedure, AC3 has no endpoint list, AC4 does not fix the status code, AC6 has no boundary cases, AC2 does not check the 10-minute lifetime; T9/T31/T26/T70/T76/T78 verifications have no AC.
  7. Cutting the audit log or the back-fill list orphans AC12/AC14; cut item 7 conflicts with AC15.
  8. Unstated: market "published time", alert edit/re-enable baseline, destination deletion, mock route collision (`/mock/slack/{name}` vs `/fail`), `Retry-After` > 60 s, revised disaster 4.6 → 6.0 never notifying, unconfirmed email addresses as an abuse vector. Missing from §8: role claim in the bearer token, the Identity endpoint hook, unused Identity endpoints' behavior, USGS publication delay vs the 2 h window.
  9. Log 14:01 still lists T50 as rejected; D12 later accepted it. Verified (line in the 14:01 entry).
  - Coverage check: all 12 defer rows appear only as out of scope or limits; rejected rows are cited only as rejected or as limits (T2, T9, T32, T59, T83). Six accept rows are not cited (T11, T21, T23, T37, T68, T82); their content is present, but T68 (per-channel formatting) is thin for Slack.
- What I did instead: Decisions on 1, 2, 4, 5 and the T33 deviation go to the user (next entry); the rest is fixed in the revision. The 14:01 entry is left as written (the log is append-only); this entry records that D12 supersedes its T50 item.
- How I verified: `grep` of the log and of the skeleton commit for findings 4 and 9; the coverage script compared the triage verdict column with T-IDs cited in `docs/spec.md`.

### [2026-10-29 16:20] D14 – Market alerts: re-arm with hysteresis and optional steps
- Type: course-correction
- What the AI proposed / generated: Raw spec §2.3: one market event per asset × direction × UTC day, re-matched on each poll; the delivery index stops repeats. The review showed it contradicts the first-seen and 2 h rules (finding 1).
- What was wrong with it / what I weighed: Reading the draft, it meant at most one notification per day per asset and direction. Not acceptable: a market that moves, recovers and moves again is news twice. I first asked for polling at least every 5 minutes; the AI pointed out that polling is already 5 minutes and the cap comes from the dedup rule, then listed options: daily cap, re-arm with hysteresis, step escalation, cooldown, own short-window calculation (would override T22).
- What I did instead: Re-arm + steps. The market source emits an event when the provider's change crosses a 0.5-point grid line between two polls. Each alert fires on crossing its threshold, re-arms when the change falls below threshold − 1 point, and with an optional `stepPercent` notifies again at each further step. A new or re-enabled alert starts disarmed if the move is already past its threshold (keeps T34). No daily key; T22 (no own price history) stays.
- How I verified: Worked example in spec §2.4 (threshold 5, step 2.5: exactly 3 notifications); AC8 tests that sequence.

### [2026-10-29 16:21] D15 – News keywords match the headline only
- Type: decision
- What the AI proposed / generated: Raw spec: keyword in title or summary. Review finding 2: common keywords flood the user, close to the rejected T1 reading.
- What was wrong with it / what I weighed: Headline-only, whole-word matching cuts most noise without throttling (T40 stays deferred). D9 (news = keywords) is unchanged.
- What I did instead: Headline only, word boundaries, case-insensitive; the remaining flood risk is a known limit in §7.
- How I verified: AC6 boundary cases (whole word, not in summary only).

### [2026-10-29 16:22] D16 – Other spec review decisions
- Type: decision
- What the AI proposed / generated: Raw spec cut item 8 ("all categories simulated, still inside the brief"), no real-Slack check, delivery key per destination instead of T33's "channel".
- What was wrong with it / what I weighed: Item 8 came from my own skeleton and contradicts T13 ("in the world"). I want to keep it as a last resort, but not disguised as inside the brief. Real Slack is never exercised by any AC. The destination key is needed because T67 allows several destinations per channel.
- What I did instead: Item 8 kept, reworded: only with my explicit approval, recorded as a weakened requirement. New optional manual AC20 (real webhook from my workspace, screenshot). Delivery unique key `(alert, event, destination)`. The other review findings were fixed in the revision without a separate decision: normal users have no role (no hook in `/register` needed), audit hooks via an endpoint filter (unverified, fallback own endpoints), unused Identity endpoints return 404, `activeSince` for edit/re-enable, soft-deleted destinations, mock routes `/hooks/{name}`, `/fail`, `/reject`, `Retry-After` cap 5 min, sharpened ACs (AC2–AC11) and new AC16–AC19 for T9/T31, T26, T70, T76/T78, cut items name the ACs they drop, §7 and §8 extended.
- How I verified: Diff `bf14840..` on `docs/spec.md`; the coverage script re-run on the revised spec.

---

## Phase 3 – architecture and slices (Claude Code session 04)

### [2026-10-29 14:59] D17 – Vertical slices instead of layered milestones
- Type: course-correction
- What the AI proposed / generated: The phase 3 plan kept the layered milestones of `docs/plan.md`: M1 skeleton and auth, M2 events and alerts, M3 delivery, M4 the whole Angular frontend, then a separate test phase. Principle 4 said "the UI comes last because it only consumes the API".
- What was wrong with it / what I weighed: With that order nothing can be checked in the browser until M4, and the tests come after all the code. I want to check every piece of work by hand before it is committed. My suggested order: database + register/login + landing page, then admin, then a notifications list in the UI, then email and Slack sending.
- What I did instead: Seven slices S1–S6 (S3 split into events and alerts) in [`docs/plan.md`](../docs/plan.md), each with backend, frontend, its own tests, a manual check and the ACs it closes. Principle 4 rewritten. The spec's §8 "check before" column now names slices instead of milestones.
- How I verified: Script over the slice table: every AC1–AC21 is assigned to at least one slice; AC9 and AC12 are split on purpose (named parts).

### [2026-10-29 14:59] D18 – Notifications page is in scope (T81)
- Type: decision
- What the AI proposed / generated: Spec §4: "user-facing notification history" (T81) and "user-facing delivery status" (T79) out of scope ("not asked"). When I asked for a notifications list in slice 3, the AI first asked whether I meant the user's alerts, the ingested events or the sent notifications.
- What was wrong with it / what I weighed: I meant the sent notifications: what went out by email or Slack must also be visible in the app under a Notifications menu item. Not a brief requirement, but it makes each slice checkable in the UI before delivery exists (S3b shows `Pending`, S4 turns it into `Sent` / `Failed`).
- What I did instead: Spec §2.12 (reads the same `Delivery` rows, own rows only, no last-error text, masked URLs), §3 scope line, T81 removed from §4, T79 narrowed to auto-pause, new AC21, new cut item 6 (dropping AC21). This is a scope addition, not a cut; rule 6 is unaffected.
- How I verified: Diff on `docs/spec.md`; AC21 assigned to S3b in the slice table.
