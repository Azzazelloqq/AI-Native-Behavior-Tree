# Agent session prompt template

Replace bracketed values and give the agent only one implementation or review card.

```text
Work on AIBT task [TASK-ID] from [CARD-PATH].

Read, in order:
1. AGENTS.md
2. Planning~/MASTER_PLAN.md
3. Planning~/AGENT_WORKFLOW.md
4. Planning~/DECISION_BOUNDARIES.md
5. Planning~/DEFINITION_OF_DONE.md
6. the task card and every normative document it references

Before editing, verify all dependencies are merged and report the allowed/forbidden paths and verification plan. Do not modify normative specifications, work-items.json, package metadata, asmdefs, changelog, or unrelated files unless the card explicitly allows it. If a decision is missing or conflicts with a specification, stop and report it; do not invent behavior.

Use an isolated branch/worktree. Implement only the card scope, test observable behavior, run every required verification, inspect the diff, and finish with the exact handoff report from AGENT_WORKFLOW.md. Do not claim a skipped verification passed.
```

For a review session append:

```text
You are the independent reviewer, not the implementation owner. Review against the task card and normative specifications. Return Accept, Changes required, or Specification conflict with evidence. Do not fix code unless separately authorized.
```
