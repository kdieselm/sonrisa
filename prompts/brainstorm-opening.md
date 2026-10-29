# Brainstorm opening prompt

Phase 1. Run in Claude Code (no BMAD, see D4 in `process/log.md`). The output is saved raw, unchanged, to `docs/brainstorm/`.

```
We are starting the design of a small product feature. Below is the product brief, exactly as the product manager wrote it:

"We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking news, market movements, natural disasters, that kind of thing. Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too."

There is no further specification: no wireframes, no schema, no agreement on what "something important" means, where the data comes from, or how events are detected.

Your job in this step is NOT to propose a solution. Generate questions and competing interpretations, so that I can decide.

For each topic below, give:
- the open questions the brief leaves,
- 2–3 competing interpretations, each with its main trade-off,
- what would make each interpretation clearly wrong for this brief.

Topics:
1. What counts as "something important"? Who decides: the user, the system, the source?
2. Where does the data come from for each category (breaking news, market movements, natural disasters)? Real feeds vs. simulated data; reliability and access (keys, rate limits).
3. How are events detected and processed: polling, push, frequency, deduplication?
4. Real-time notifications vs. digests.
5. What does the admin do? What does the admin view show and allow?
6. User roles and user management.
7. Channels: what does "flexible enough to add more channels later" require in practice?
8. Scale: how many users, alerts, events; what matters at MVP size and what does not?
9. Anything else in the brief that is ambiguous and I have not listed.

Constraints:
- This is an MVP built in a few hours. It must still be a working end-to-end implementation.
- Every requirement in the brief is mandatory: users setting up alerts, important world events (all three named examples), email and Slack, extensible channels, admin view. Do not suggest dropping or weakening any of them. If an interpretation would do that, mark it as such.

End with a short list of the 5 decisions you think I must make first, ordered by how much they affect the rest of the design.
```
