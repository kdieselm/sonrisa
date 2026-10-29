# Template: task

Use this to start a milestone or any piece of implementation work.

```
Task: <milestone / feature name>

Context:
- Requirements: docs/spec.md, sections <…> (traceability rows: <…>)
- Architecture decisions: docs/architecture.md, ADR <…>
- Every requirement in docs/brief.md is mandatory. If this task would drop or weaken any of them, flag it and stop.

Goal:
<what must be true when this is done>

Constraints:
- No new NuGet/npm package without my approval. Name the exact package and version, and confirm it exists.
- Follow the conventions in CLAUDE.md.

First write a plan of 5–10 lines: files you will create or change, the order, and how you will verify the result. Do not write code until I approve the plan.
```
