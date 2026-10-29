# Brainstorm – raw AI output, fresh context (unedited)

- **Prompt:** the code block of [`prompts/brainstorm-opening.md`](../../prompts/brainstorm-opening.md), verbatim, plus one wrapper instruction: "Answer the prompt below directly in your final message, in Markdown. Do not use any tools, and do not read any files: answer only from the prompt text itself. Your final message must contain only the answer, with no preamble or closing remarks."
- **Tool / model:** Claude Code subagent (general-purpose), started from session 01 with no conversation history, no planning handoff and no repo files
- **Date:** 2026-10-29
- **Why a second run:** the first run ([`01-raw-output.md`](01-raw-output.md)) happened in a context that already knew the plan. This run is the unbiased baseline. Comparison in `process/log.md`.
- **Status:** raw. Nothing below has been edited, accepted or rejected yet. Facts in it (rate limits, keyless APIs, update intervals) are unverified.

---

# Alerts feature: open questions and competing interpretations

**Legend:** ⚠️ **Weakens a mandatory requirement.** You can still pick it, but only as one part of a design that meets the requirement some other way.

---

## 1. What counts as "something important"? Who decides?

**Open questions**
- Is "important" a property of the event itself (a magnitude-7 quake matters to everyone) or of the user (only AAPL matters to me)?
- What can a user set? Category only? Keywords, tickers, regions? Numeric thresholds such as % change or magnitude?
- Does each category have its own rule shape, or is there one generic rule shape for all of them?
- Is there a global floor (a minimum severity) that users cannot go below? Can the admin tune it?
- "That kind of thing": is the category list fixed at the three named ones, or open-ended and extendable by the admin?
- Should a user be able to preview what a rule would have matched recently?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Source decides.** The user subscribes to a category, and whatever the source flags fires: a "breaking" tag, USGS significance, a GDACS alert level. | Simplest to build and easy to explain. But the user has almost no control, it can be noisy or coarse, and "important" means something different in each source. | Users need targeting (a specific ticker or region). "Set up alerts" then shrinks to a category on/off toggle, which ⚠️ thins the "users set up alerts" requirement. |
| **B. User decides, per-category rules.** News: keywords or topics. Markets: symbol plus % move in a time window. Disasters: type, region, minimum severity. | Most useful and testable. Costs three rule schemas, three forms and three matchers. | The source data has no structured fields to filter on (for example, news with no topic or region). Also wrong if build time can't cover three rule types. |
| **C. System decides.** A global importance score (heuristics or an LLM) with admin-tuned thresholds. Users only opt in. | Consistent and centrally controlled. But it is opaque, hard to test, not deterministic, and adds LLM cost and latency. Risky in a few hours. | Users get no say beyond on/off (⚠️ same thinning as A). Also wrong if the result can't be reproduced in a demo. |
| *(Hybrid B+C)* A system or admin floor, plus user filters on top. | Prevents noise and keeps user control. Needs two layers of logic. | The floor hides events a user explicitly asked for. |

---

## 2. Where does the data come from?

**Open questions**
- Real data in the MVP, simulated, or both?
- Are API keys available? Who owns them? Where are they stored (env vars or admin-configured)?
- Do licences or terms of service allow redistributing headlines or prices in notifications?
- **Markets:** which asset classes (equities, indices, FX, crypto)? Equities are closed nights and weekends, so a demo may show no movement. Is a 15-minute data delay acceptable?
- **News:** which languages and regions? What counts as "breaking"?
- **Disasters:** which types (earthquakes are easy, floods and storms are harder)? Global or one country?
- Must the demo reliably trigger an alert on demand?

**Candidate sources (to check, not decided)**
- **News:** RSS feeds, GDELT (free, no key, about 15-minute updates), NewsAPI or GNews (key needed, restrictive free tiers).
- **Markets:** Finnhub (key, about 60 requests/min), Alpha Vantage (key, very low daily quota), CoinGecko (crypto, no key needed for basic use).
- **Disasters:** USGS earthquake GeoJSON (free, no key, reliable), GDACS (multi-hazard), NASA EONET, NWS (US only).

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Real feeds only, all three categories.** | Authentic. But keys, rate limits and terms of service apply, nothing may happen during a demo, and the system is hard to test deterministically. | No keys or licence are available, or the demo or acceptance test needs a guaranteed trigger. |
| **B. Simulated generators only.** | Deterministic, fast and free. ⚠️ Doesn't meet "something happens *in the world*". | Used as the only source. It is acceptable only as a test or demo supplement. |
| **C. Pluggable source adapters.** At least one real source per category (keyless where possible), plus a simulated or admin "inject test event" source. | Meets the requirement and stays demoable. Costs more adapter code and a normalized event schema. | No real source per category is reachable in the time available. Also wrong if the injected events can't be told apart from real ones in the logs, notifications and admin view. |

---

## 3. How are events detected and processed?

**Open questions**
- What is an "event"?
  - News: a single article, or a story clustered across many articles?
  - Markets: a stream of prices, where the "event" has to be *computed* (a % change against what baseline, over what window?). That needs state.
  - Disasters: a discrete record, but it may be revised later (a quake's magnitude gets updated).
- How often is each source polled, within its rate limits?
- Does any source offer push (websockets, webhooks)?
- **Deduplication** at three levels:
  - same item from the same source (external ID);
  - same real-world event from several sources (a quake reported by both USGS and GDACS, one story from ten outlets);
  - same notification to the same user (user × event × channel).
- How do updates work (re-notify on a magnitude revision?), plus cooldowns and re-arming for threshold alerts (notify once per crossing)?
- On first start or restart, how do we avoid flooding users with historical events (backfill policy)?
- Retries and failure handling: does a failed send block other sends?
- Where does processing run: an in-process scheduler, a separate worker, or cron?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. In-process scheduler.** Polls each adapter on its own interval, then: normalize → upsert on (source, external_id) → match rules → write delivery records → send. | Fast to build with few moving parts. Single process, and work in flight is lost on crash unless deliveries are persisted first. | Polling exceeds rate limits. Also wrong if there is no unique delivery key, so a restart re-sends everything. |
| **B. Push or streaming ingestion** (websockets, webhooks). | Lowest latency. Few free sources support it, connections need managing, and reconnect logic is required. | The chosen sources don't offer push. That is likely for news and disasters. |
| **C. Ingestion worker plus job queue** (Redis/BullMQ, Celery, etc.). | Robust, with retries and independent scaling. Extra infrastructure and setup time. | It can't be stood up within the MVP time box. Also wrong if the scale doesn't justify it (see topic 8). |

---

## 4. Real-time notifications vs. digests

**Open questions**
- Does "notified when something happens" imply immediacy? What latency is acceptable: seconds, minutes, or one polling interval?
- Is the choice made per user, per alert, or per channel?
- Quiet hours, time zones for digests, and caps (at most N per hour)?
- What happens when a burst of matches arrives (a market crash, an aftershock sequence)?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Real-time only** (latency bounded by polling). | Matches the brief's wording, with the least scheduling logic. Risk of spam in noisy categories. | Rules are broad, so users get flooded. That is likely with keyword news alerts. |
| **B. Digest only** (hourly or daily). | Low noise and simple batching. ⚠️ Contradicts "breaking news" and "when something happens". | It is the only mode. |
| **C. Real-time by default, with throttling and overflow batching**, optionally a digest choice per alert. | Covers both needs. Adds a scheduler for digests, cooldown state and more test cases. | Throttling silently drops events instead of batching them. Also wrong if the digest scheduler doesn't fit in the time budget, which pushes the design back to A. |

---

## 5. The admin view

**Open questions**
- Who is "the admin"? An internal operator of the product, or an admin at a customer organisation?
- Read-only monitoring, or management?
- Possible scope:
  - users (list, disable, change role);
  - all alerts (view, pause);
  - sources (health, last poll, errors, enable/disable, keys, intervals);
  - events (recent ingested events);
  - deliveries (log, status, failures, retry);
  - channel configuration (SMTP, Slack app);
  - global thresholds and categories;
  - injecting test events;
  - an audit log.
- Does the admin view have to be protected by real authorisation?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Operations dashboard, mostly read-only.** Source health, recent events, delivery log with failures, users and alerts list, plus a few actions (pause alert, retry, inject test event). | Cheap and immediately useful for the demo and debugging. Limited control. | The PM expects admins to *configure* the system (thresholds, sources, categories). |
| **B. Management console.** Full CRUD over users and roles, alerts, sources and keys, thresholds, categories and channel configuration. | Powerful. Many forms, a larger security surface, and it eats MVP time. | It crowds out the end-to-end user flow in a few-hour build. |
| **C. Tenant/org admin.** A customer admin manages their team's members, shared Slack workspace and team alerts. | Realistic for B2B and Slack. Needs multi-tenancy, which is a big scope increase. | The product is single-tenant or consumer-facing. |
| *(Anti-pattern)* A raw database browser or an unauthenticated admin page. | Fastest. ⚠️ An unsecured "admin view" barely meets the requirement. | Anything is exposed beyond localhost. |

---

## 6. User roles and user management

**Open questions**
- Is this a feature inside an existing product with existing auth, or a standalone app?
- Signup: open, invite-only, or seeded accounts?
- Login method: password, magic link, or SSO?
- Roles: just user and admin, or more (viewer, org admin)?
- Do alerts belong to individuals or to teams? This matters because a Slack channel is usually shared.
- Must email addresses and Slack destinations be verified before sending?
- Can admins act as, or edit on behalf of, users?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Single tenant, two roles** (user, admin). Simple auth, admin seeded. | Fast and enough for the brief. No teams or sharing. | Alerts are meant to be team-owned, for example posting to a team Slack channel managed jointly. |
| **B. Organisations or teams** with org admins and members. Alerts and destinations owned by the team. | Matches Slack's natural model. Multi-tenancy throughout the schema and authorisation. | The brief's "users" are individuals, so B is overbuilt for a few hours. |
| **C. Stubbed identity** (a user picker, or a hardcoded current user). | Fastest. ⚠️ Admin separation is cosmetic, and "users" isn't really implemented. | The MVP is shown to anyone as "working end-to-end", or deployed beyond local. |

---

## 7. Channels and "flexible enough to add more channels later"

**Open questions**
- **Email:**
  - Must mail reach a real inbox (SMTP, SendGrid, SES, with sender domain setup), or is a dev catcher (Mailpit, Ethereal) acceptable as "working"?
  - Sending quotas.
  - An unsubscribe link (CAN-SPAM, GDPR).
- **Slack:**
  - Incoming webhook URL pasted by the user: easy to build, but the user must create a webhook, which is friction.
  - A Slack app with OAuth and a bot token: better UX and DMs are possible, but it needs a public redirect URL and app setup.
  - Send to a channel or a direct message?
  - Slack's rate limit is about 1 message/sec per webhook.
- Can one alert send to several channels? Can a user have several destinations per channel?
- What are the likely next channels (SMS, push, Teams, Discord, generic webhook)? Each has a different configuration shape.
- Does "flexible" mean *code-level* (a developer adds a class) or *runtime* (the admin enables a new channel type without a deploy)?
- How does each channel format the message (HTML email vs Slack blocks)? How are failures and retries handled per channel?
- Should destinations be verified ("send test message")?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Code-level channel interface plus registry.** Each channel has `validate(config)`, `render(notification)` and `send(...)`. Destinations are stored generically as `{type, config JSON}`. Matching produces channel-neutral notifications. | Cheap, and adding a channel is one new adapter. It doesn't allow adding channels without a deploy. | The PM means admins enable new channels at runtime. |
| **B. A plus a generic outbound webhook channel.** | Any third party can integrate without new code. End users still can't use it directly (it's for developers and integrations). | Treated as a *replacement* for real Slack or email adapters (⚠️). |
| **C. Runtime plugin system** (dynamically loaded or configured channel types). | Maximum flexibility. Overengineered and slow to build. | The time box is hours, which it is. |
| *(Anti-pattern)* Hard-coded `if email … elif slack` in the matching logic, or `email`/`slack_url` columns on the alert table. | Fastest. ⚠️ Fails "flexible enough to add more channels". | Always wrong for this brief. |

---

## 8. Scale

**Open questions**
- How many users and alerts per user: demo (10s), pilot (1,000s), or production?
- Incoming event volume:
  - USGS reports hundreds of quakes per day at all magnitudes;
  - news can be thousands of items per day;
  - market prices × number of symbols.
- Fan-out per event (one event × N matching users × M channels).
- Email provider quotas, Slack limits, and API quotas *per poll*: market alerts across many symbols multiply calls.
- How long are events and the delivery history kept?

| Interpretation | Main trade-off | Clearly wrong if… |
|---|---|---|
| **A. Demo scale.** SQLite or a single Postgres, in-process scheduler, a naive "check every rule against every new event" loop. | Fast and simple. Not production-grade. | Real users above a few hundred, or high-volume sources such as unfiltered news. |
| **B. Small production.** Postgres, a delivery queue, indexed matching (by category, symbol, region), retries. | Realistic headroom. More infrastructure and time. | Only a demo is needed. |
| **C. Large scale.** Streaming pipeline, sharded matching, multi-region. | Future-proof. | A few-hour MVP, which it is. Always wrong here. |

**What matters at MVP size:** correct deduplication and idempotent sends; not spamming (cooldowns, backfill policy); staying within *source* rate limits and *provider* sending quotas; a visible delivery and error log.

**What doesn't matter yet:** horizontal scaling, matching performance, high availability, multi-region.

---

## 9. Other ambiguities in the brief

- **Product context:** "a small product feature" suggests an existing product. Is there an existing stack, auth and user base to plug into, or is this a standalone app?
- **"Set up alerts" lifecycle:** create, edit, pause, delete, test-send? Is there a user-facing notification history?
- **Notification content:** headline plus link? Snippet? Price chart? Map? Source attribution and licensing for redistributed content.
- **Safety and liability:**
  - Disaster alerts must not look like official emergency warnings. A delay could matter to someone. A disclaimer may be needed.
  - Market alerts may need a "not financial advice" note and a statement about data delay.
- **Geography, language and time zones:** "the world" is unbounded.
- **Compliance:** email unsubscribe; consent for messages; storing Slack tokens or webhooks as secrets.
- **Definition of "working end-to-end":** real email in a real inbox and a real Slack workspace, or dev substitutes? Who provides the Slack workspace and email domain?
- **Failure surfacing to users:** a revoked Slack webhook or a bounced email. Is the user told? Is the alert auto-paused?
- **Acceptance criteria:** none given. What demo scenario proves it works (for example, "inject a magnitude-6 quake in Japan → user X gets an email and a Slack message within 1 minute; the admin sees the delivery")?
- **Ownership of thresholds:** if the admin can change global thresholds, do existing user alerts change behaviour silently?

---

## The 5 decisions to make first (highest impact first)

1. **The importance and alert-rule model (topic 1):** who decides, and what a rule looks like for each category. It drives the schema, the user UI, the matching logic and what the sources must provide.
2. **The data source strategy for each category (topic 2):** real adapters plus a test-event injector, or something else, and which concrete sources and keys. It decides the normalized event schema, detection (polling, computing market moves), deduplication and whether the demo is reliable.
3. **Product context, tenancy and roles (topics 6, 9):** standalone or inside an existing app; single-tenant user/admin or organisations. It sets the auth work, admin scope and who owns a Slack destination.
4. **Channel mechanics and the meaning of "end-to-end" (topic 7):** a Slack webhook or a Slack OAuth app; real email or a dev catcher; and the channel adapter contract with generic destination storage. It decides integration effort and how extensibility is proven.
5. **Delivery semantics (topics 3, 4):** real-time with throttling or a digest option; the deduplication, cooldown and backfill rules. It sets the scheduler design, the delivery data model and whether users get spammed.

*(The admin view scope, topic 5, comes right after. It mostly follows from decisions 2, 3 and 5.)*
