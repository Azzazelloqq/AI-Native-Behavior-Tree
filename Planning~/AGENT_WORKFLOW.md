# Agent workflow

## Before accepting a task

1. Read the required documents listed in `MASTER_PLAN.md`.
2. Confirm every dependency in the task card is complete.
3. Inspect repository status and preserve unrelated work.
4. State the task ID, assumptions, allowed paths, and verification plan before editing.

If a normative choice is missing, stop and return a proposed decision. Do not select a convenient behavior in code.

## Branching

A branch per task is still useful for keeping a change reviewable and easy
to back out:

```text
branch: task/<task-id-lowercase>-<short-name>
commit: <area>: <imperative outcome>
```

Do not update submodules, package versions, or generated project files unless the card explicitly permits it.

## Implementation loop

1. Add or identify behavior tests from the card's acceptance criteria.
2. Implement the smallest scope that satisfies them.
3. Run focused verification.
4. Run required broader verification.
5. Inspect diff for unrelated or generated changes.
6. Update only documentation explicitly owned by the task, plus any
   integration-owned shared files (package metadata, changelog, asmdefs,
   `work-items.json`) the task's own results require updating.
7. Self-check against the task card and `DEFINITION_OF_DONE.md` before
   marking the task `Done`: negative cases, scope boundaries,
   allocation/performance claims where relevant, and whether the change
   introduced a new undocumented decision that should have been escalated
   instead.
8. Produce the session summary.

Tests describe expected behavior. Never change an expectation merely because the current implementation differs.

## Stop conditions

Stop without speculative implementation when:

- a dependency is missing or incomplete;
- a requirement conflicts with a normative specification;
- a public contract must change outside the assigned task;
- required toolchain, credentials, hardware, or platform access is unavailable;
- acceptance cannot be verified safely.

Report the exact blocker, evidence, and smallest decision or user action needed.

## Session summary

Every task ends with a summary — useful for picking the work back up in a
later session as much as for reporting it now:

```text
Task ID:
Outcome:
Branch and commit:
Files changed:
Observable behavior implemented:
Verification commands and results:
Benchmarks and environment, if required:
Deviations from card: none | details
Normative questions discovered: none | details
Known limitations within scope:
Recommended next task:
```

Do not claim completion if a required command was skipped. State `not run` and why.
