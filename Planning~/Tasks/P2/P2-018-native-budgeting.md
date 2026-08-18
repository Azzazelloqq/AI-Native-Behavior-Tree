# P2-018 — Native deterministic step budgeting and resume

Status: `Done`

## Objective

Preserve every native execution cursor across deterministic step-limited segments with frozen input and exact P1 accounting.

## Depends on

- `P2-014`.
- `P2-015`.
- `P2-016`.
- `P2-017`.

## Required reading

- `Documentation~/specifications/reference-executor-machine-v1.md`
- `Documentation~/specifications/platform-backends-v1.md`
- `Documentation~/specifications/update-phases-v1.md`

## Allowed changes

- `Runtime/Execution/Native/Budgeting/`
- `Tests/Runtime/NativeExecution/Budgeting/`

## Forbidden changes

- Wall-clock policy, new input on Resume, uncounted semantic work, hidden callbacks, or budget as a node status.

## Deliverables

- Native limited/unlimited driver and segment/cumulative metrics.

## Acceptance criteria

- Budget zero, every semantic split, repeated Resume, exact exhaustion, no-work close, abort while suspended, and illegal API paths match P1.
- Input, time, snapshot revision, completion set, and reactive preparation remain frozen across Resume.
- Observer evaluation is one explicit step; trace/command effects are not split from their callback step.
- Unlimited and every positive partition produce identical semantic result/state/commands/diagnostics/trace after filtering only budget control events.
- Step/counter overflow faults structurally without managed exception or wrap.

## Required verification

```text
P1 partition-equivalence matrix
leaf/composite/reactive/parallel/decorator/observer/async split tests
zero-budget callback/command absence tests
allocation/Burst checks
```

## Handoff notes

- Web uses this semantic engine through P2-024; no public Burst-direct Web policy is introduced here.
