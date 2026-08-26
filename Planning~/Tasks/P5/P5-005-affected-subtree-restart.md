# P5-005 — Affected-subtree restart

Status: `Draft`

## Objective

Implement the narrower-blast-radius reload strategy: restart only the
subtree(s) `P5-003`'s classifier localizes an incompatible change to,
preserving the rest of a live instance's state. Falls back to `P5-004`'s
full restart whenever localization is not possible.

## Depends on

- `P5-003` (classifier and localization data).
- `P5-004` (full-restart mechanism this card falls back to).

## Required reading

- `P5-001`'s accepted ADR.
- `Documentation~/hot-reload.md`'s "Reload strategies" section.
- `Documentation~/specifications/execution-semantics-v1.md`,
  `Documentation~/specifications/reference-executor-machine-v1.md` (what a
  subtree boundary actually means for lifecycle/composite state).

## Allowed changes

- `Runtime/HotReload/SubtreeRestart/` (new, or wherever `P5-001`'s ADR
  places it).
- `Tests/Runtime/HotReload/SubtreeRestart/` (new).

## Forbidden changes

- `P5-006`'s migration mechanism.
- Any change to `P5-004`'s full-restart contract other than calling it as a
  fallback.
- Silently widening the restart blast radius beyond what `P5-003` localized
  "to be safe" -- if localization is uncertain, fall back to full restart
  explicitly rather than guessing a wider-but-unproven subtree boundary.

## Deliverables

- A function that restarts exactly the subtree(s) `P5-003` localized,
  reusing `P5-004`'s teardown/rebuild mechanism scoped to that subtree,
  while leaving sibling subtrees' live state untouched.
- Explicit fallback to `P5-004`'s full restart whenever localization is
  ambiguous, spans the tree root, or crosses a boundary the classifier
  cannot prove isolated (e.g., a shared-blackboard write visible outside the
  subtree).
- Structured reporting of exactly which subtree(s) restarted, for `P5-008`.

## Acceptance criteria

- A change localized to one subtree leaves every sibling subtree's
  observable state (including any shared-blackboard values it legitimately
  owns) provably unchanged across the restart.
- A change that legitimately affects shared state outside the localized
  subtree (per `Documentation~/specifications/agent-shared-blackboard-v1.md`'s
  conflict-policy rules) is never subtree-restarted -- it falls back to full
  restart, proven by a negative test.
- Repeated subtree restarts targeting different subtrees in sequence never
  leave the instance in a state divergent from an equivalent full restart of
  the whole instance (a golden-equivalence proof, mirroring `P4-003`'s
  `PipelinedJobs` equivalence technique).

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <subtree-restart fixture>
golden-equivalence proof against an equivalent full restart
negative test: shared-state-crossing change falls back to full restart
```

## Handoff notes

- `P5-007` verifies subtree restart does not desynchronize a scheduler's
  per-instance work-estimate state from the instance's actual current shape.
- `P5-008` needs this card's per-subtree reporting to show the user "only
  this part of the tree reloaded," not a blanket "reloaded" notice.
