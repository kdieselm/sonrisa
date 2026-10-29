# Brainstorm – raw AI output (unedited)

- **Prompt:** [`prompts/brainstorm-opening.md`](../../prompts/brainstorm-opening.md), used verbatim
- **Tool / model:** Claude Code, Claude Opus 5.5 (session 01)
- **Date:** 2026-10-29
- **Status:** raw. Nothing below has been edited, accepted or rejected yet. Review happens in phase 2 and is logged in `process/log.md`.
- **Known limitation of this run:** it ran in the same session that had already read the planning handoff, so the answers may lean toward the decisions already made there. See the log entry of the same date.

---

## 1. What counts as "something important"?

**Open questions**
- Is importance a property of the event (objective), of the user's interest (subjective), or both?
- Who sets the bar: the user per alert, an admin globally, or the source itself?
- Does "important" mean "rare" (top 1% of events) or "relevant to me" (any event matching my topic)?
- Is importance comparable across categories? Is a 6.0 earthquake "as important" as a 5% stock drop?
- Should importance change over time (an event that escalates)?

**Competing interpretations**
- **A. User-defined filters only.** Every event that matches the user's rule (topic, keyword, region) is important to that user. *Trade-off:* simple and transparent, but "important" collapses into "matching"; noisy feeds (news) produce floods of notifications.
- **B. System-scored severity + user threshold.** Each source maps its events to a common score (e.g. 0–100); the user picks a minimum. *Trade-off:* one consistent knob across categories, but the mapping per source is a judgment call that must be explained and is hard to calibrate (especially for news).
- **C. Admin-curated importance.** Admins mark events (or sources/keywords) as important; users subscribe to categories. *Trade-off:* high signal, but needs a human in the loop, and is not "real-time"; the admin view becomes an editorial tool.

**What would make each clearly wrong**
- A is wrong if the result is that users are notified about everything in a category — the brief says "something important", not "everything".
- B is wrong if the scoring is opaque or arbitrary with no documented rule, or if it is so coarse that every news item gets the same score.
- C is wrong if users cannot set up alerts themselves (the brief says *users* set them up), or if nothing is delivered without an admin acting.

## 2. Where does the data come from?

**Open questions**
- Real data or simulated? If real: which providers, with or without API keys, and what are the terms of use?
- Does the reviewer need internet access, API keys or accounts to see the system working?
- How reliable must sources be for an MVP? What happens when a feed is down?
- Is one source per category enough?

**Competing interpretations**
- **A. Real public feeds for every category.** Disasters: USGS earthquake GeoJSON feed (keyless, well documented); optionally GDACS. News: RSS feeds from major outlets or Google News RSS. Market: a free market-data API (most need a key: Alpha Vantage, Finnhub, Twelve Data; keyless options are unofficial and break). *Trade-off:* realistic, but market data typically needs a key and has rate limits; demo depends on the real world producing events while the reviewer looks.
- **B. Simulated sources only.** A generator or admin form produces events for all three categories. *Trade-off:* fully deterministic and demoable offline, but "something happens in the world" is never actually observed; looks like a toy.
- **C. Hybrid.** Real feeds where they are keyless and stable (USGS, RSS), simulated where they are not (market), plus a simulator for all categories so the reviewer can trigger a guaranteed match. *Trade-off:* best demo story, but two code paths for ingestion and the market category is "fake" — needs a clear justification.

**What would make each clearly wrong**
- A is wrong if the reviewer cannot run it without signing up for keys, or if the demo silently shows nothing because no real event crossed the threshold.
- B is wrong if presented as covering "the world" without saying it is simulated — and arguably wrong if a real, keyless source was easily available.
- C is wrong if the simulated category is hidden or if the abstraction does not let a real market source be dropped in later.

## 3. How are events detected and processed?

**Open questions**
- Polling vs. push (webhooks/streams)? How often?
- How are duplicates detected (same event seen in two polls, same story from two feeds)?
- When does matching happen — at ingestion, or on a schedule?
- Updates to an existing event (USGS revises magnitudes) — new notification or not?
- What happens to events ingested before an alert existed?

**Competing interpretations**
- **A. Periodic polling + match at ingestion.** A background worker polls each source every N minutes, upserts events by external ID, and matches new events against active alerts immediately. *Trade-off:* simple, predictable; latency = poll interval; revisions need explicit handling.
- **B. Event queue / pipeline.** Sources push into a queue (in-memory channel, Redis, a broker); separate consumers match and deliver. *Trade-off:* scalable and decoupled, but heavy for an MVP and adds infrastructure.
- **C. Match on a schedule (batch).** Events are stored as they come; a job runs every N minutes and matches everything new since the last run. *Trade-off:* easy to make idempotent with a watermark, but adds latency and a second timer.

**What would make each clearly wrong**
- A is wrong if there is no deduplication (every poll re-notifies the same earthquake).
- B is wrong for this scope if it adds a broker the reviewer must run without a clear benefit.
- C is wrong if the watermark logic can skip or repeat events.

## 4. Real-time notifications vs. digests

**Open questions**
- Does "get notified when something happens" imply immediacy?
- Would users prefer a daily summary for low-severity items?
- How to avoid notification storms (e.g. an aftershock sequence)?

**Competing interpretations**
- **A. Real-time only (one notification per matching event).** *Trade-off:* matches the brief's wording directly; can be noisy.
- **B. Digest only (hourly/daily).** *Trade-off:* quiet, but contradicts "when something important happens"; breaking news in a daily digest is not breaking.
- **C. Per-alert choice: immediate or digest.** *Trade-off:* most flexible; doubles the delivery logic and the UI.

**What would make each clearly wrong**
- A is wrong if there is no protection at all against duplicates or storms.
- B is wrong as the only mode: it weakens "get notified when something important happens".
- C is wrong for an MVP if it delays the end-to-end path; it is a reasonable later extension.

## 5. What does the admin do?

**Open questions**
- Is the admin an operator (monitoring, troubleshooting) or an editor (curating events) or a user manager?
- Can admins see and edit other users' alerts? Privacy implications?
- Do admins manage sources and channels (enable/disable, configure)?
- How does someone become an admin?

**Competing interpretations**
- **A. Operational view.** Read-mostly: users and their alerts, ingested events, delivery log with status/errors/retries, plus a way to trigger a test/simulated event. *Trade-off:* directly useful for running the system and for demoing; little editing.
- **B. Management console.** Everything in A plus managing users (disable, change role), sources (enable/disable, polling interval, thresholds) and channels. *Trade-off:* richer, but large UI surface.
- **C. Editorial console.** Admins create or promote events and decide what is important. *Trade-off:* ties into importance interpretation 1C; makes the admin the bottleneck.

**What would make each clearly wrong**
- A is wrong if it is only a raw table dump with no way to understand why a delivery failed.
- B is wrong for this scope if it crowds out the user-facing core.
- C is wrong if the system cannot notify anyone without an admin.

## 6. User roles and user management

**Open questions**
- Self-registration or invite-only?
- Just "user" and "admin", or more (e.g. team/org owner)?
- Are alerts personal, or shared within a team (Slack channels are often team-wide)?
- Email verification, password reset, SSO?
- How is the first admin created?

**Competing interpretations**
- **A. Self-registration, two roles (User, Admin), seeded first admin.** *Trade-off:* minimal and complete for the brief; no teams.
- **B. Organizations/teams with members.** Alerts belong to a team; Slack destinations are team resources. *Trade-off:* closer to real Slack usage, but multiplies the data model and authorization.
- **C. No real auth; pick a user from a list.** *Trade-off:* fastest, but the brief says "users … set up alerts"; without real users there is no meaningful isolation and the admin role is meaningless.

**What would make each clearly wrong**
- A is wrong if users can see each other's alerts or if admin endpoints are reachable by normal users.
- B is wrong for an MVP if it delays the core path; it does not add a brief requirement.
- C weakens the brief ("users to be able to…") and should be rejected.

## 7. Channels and "flexible enough to add more channels later"

**Open questions**
- Where does per-user channel configuration live (email address = account email? Slack webhook URL per alert or per user?)
- Does "add more channels later" mean "without changing core code", "with a new class and one registration", or "configurable at runtime by admins"?
- Does each channel need its own retry/failure semantics?
- Message formatting per channel (HTML email vs. Slack blocks)?

**Competing interpretations**
- **A. Strategy interface + DI.** `INotificationChannel` with `Send(notification, config)`; each channel is a class registered in DI; per-channel config stored as JSON. Adding a channel = one class + registration + UI form. *Trade-off:* simple and conventional; still needs a deploy for a new channel.
- **B. Plugin system.** Channels discovered at runtime from assemblies. *Trade-off:* "more flexible" on paper; complex, rarely needed, harder to test.
- **C. Outsource to a notification service** (e.g. a third-party notification platform). *Trade-off:* many channels for free, but hides exactly the design the brief asks for and needs an external account.

**What would make each clearly wrong**
- A is wrong if the core delivery code still has `if (channel == "email") … else if (channel == "slack")` branches.
- B is wrong for this scope: complexity without a requirement behind it.
- C is wrong if the reviewer needs an account, and arguably sidesteps "make it flexible".

## 8. Scale

**Open questions**
- Tens of users or millions? Events per minute?
- Does matching need to be indexed, or is "loop over active alerts" fine?
- Do delivery rates hit external limits (Slack webhook rate limits, SMTP limits)?

**Competing interpretations**
- **A. MVP scale (tens of users, hundreds of alerts, hundreds of events/day).** In-process matching over all active alerts is fine. *Trade-off:* simple; documented as a known limit.
- **B. Design for growth now** (partitioned matching, queues, rate limiting). *Trade-off:* premature for a few-hour MVP.
- **C. MVP implementation with explicit seams** (delivery table as a queue, idempotent workers, matching behind an interface) so it can scale later. *Trade-off:* a little more structure, but it is the same structure needed for retries and deduplication anyway.

**What would make each clearly wrong**
- A is wrong if the limit is not written down.
- B is wrong if it costs the end-to-end path.
- C is wrong if the "seams" are speculative abstractions with one implementation and no reason.

## 9. Other ambiguities in the brief

- **"In the world"** — global only, or should users filter by region/country? Region is natural for disasters, unclear for news and markets.
- **"Breaking news"** — breaking implies recency and novelty; how old can an item be and still notify? First-seen vs. published time?
- **"Market movements"** — movement of what (indices, single stocks, crypto, FX) and relative to what (% change over which window)? A movement is a *derived* event, not a feed item.
- **"That kind of thing"** — is the category list open? Should categories be data, not an enum?
- **"Set up alerts"** — is there a test/preview ("what would this alert have matched last week")?
- **"Should work for both email and Slack"** — per alert both, or user picks one? Slack to a channel or a DM?
- **Notification content** — what must a notification contain (title, severity, link, why it matched)?
- **Unsubscribing** — one-click disable from the email/Slack message?
- **Failure visibility for users** — does a user see that their Slack webhook is broken, or only the admin?
- **Time zones and quiet hours.**

---

## The 5 decisions to make first (by impact on the rest of the design)

1. **Definition of "important"** (topic 1) — determines the event model (is there a severity score?), the alert rule shape and the matching logic.
2. **Data sources per category, real vs. simulated** (topic 2) — determines ingestion code, demo reliability, and what the reviewer needs to run it.
3. **Detection and processing model** (topic 3) — polling interval, deduplication key, when matching runs; drives the background worker and the database constraints.
4. **Channel abstraction and where channel config lives** (topic 7) — determines the delivery pipeline, the data model for per-user/per-alert destinations, and how "add more channels later" is demonstrated.
5. **Admin scope** (topic 5) — operational view vs. management console; determines roles, authorization rules and a large share of the UI work.
