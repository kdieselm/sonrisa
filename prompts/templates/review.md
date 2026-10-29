# Template: review

Use this on a diff before committing a milestone. Prefer a fresh session or subagent so the reviewer did not write the code.

```
Review this diff: <git diff range or file list>

Check only for problems in these areas:
1. Error handling – unhandled exceptions, swallowed errors, missing failure status, wrong HTTP codes.
2. Authorization – endpoints without [Authorize], admin endpoints reachable by normal users, data of one user reachable by another (including by ID).
3. Idempotency – double delivery, duplicate events, retries that repeat side effects, missing unique indexes.
4. Tests – missing tests for the cases above, tests that cannot fail, tests that test the mock instead of the code.
5. Anything that contradicts docs/brief.md or docs/spec.md.

List problems only. For each one: file and line, what is wrong, a concrete failure scenario, and a suggested fix. Do not list things that are fine. If you are not sure something is a problem, say so instead of stating it as fact.
```
