# Agent workflow

## Before accepting a task

1. Read the required documents listed in `MASTER_PLAN.md`.
2. Confirm every dependency in the task card is merged into the target branch.
3. Confirm no other agent owns the same task or exclusive write paths.
4. Inspect repository status and preserve unrelated work.
5. State the task ID, assumptions, allowed paths, and verification plan before editing.

If a normative choice is missing, stop and return a proposed decision. Do not select a convenient behavior in code.

## Isolation

Use one branch and preferably one worktree per task:

```text
branch: task/<task-id-lowercase>-<short-name>
commit: <area>: <imperative outcome>
```

Do not run concurrent task agents in the same working tree. Do not update submodules, package versions, or generated project files unless the card explicitly permits it.

## Implementation loop

1. Add or identify behavior tests from the card's acceptance criteria.
2. Implement the smallest scope that satisfies them.
3. Run focused verification.
4. Run required broader verification.
5. Inspect diff for unrelated or generated changes.
6. Update only documentation explicitly owned by the task.
7. Produce the handoff report.

Tests describe expected behavior. Never change an expectation merely because the current implementation differs.

## Stop conditions

Stop without speculative implementation when:

- a dependency is missing or not merged;
- a requirement conflicts with a normative specification;
- a public contract must change outside the assigned task;
- required toolchain, credentials, hardware, or platform access is unavailable;
- another agent modified an exclusive path;
- acceptance cannot be verified safely.

Report the exact blocker, evidence, and smallest decision or user action needed.

## Handoff report

Every task handoff contains:

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
Recommended integration order:
```

Do not claim completion if a required command was skipped. State `not run` and why.

## Review agent

The reviewer verifies the task card and normative behavior, not the implementation author's narrative. The reviewer checks negative cases, scope boundaries, allocation/performance claims where relevant, and whether the change introduced a new undocumented decision.

Review outcomes are `Accept`, `Changes required`, or `Specification conflict`. Only accepted work proceeds to integration.

## Integration agent

The integration agent owns shared-file updates, resolves only mechanical conflicts, runs the phase-level suite, and rejects semantic conflict resolution without an explicit decision. Integration does not expand task scope.
