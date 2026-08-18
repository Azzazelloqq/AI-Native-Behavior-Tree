# P2-020 — Native behavior-case equivalence and public custom-node golden

Status: `Done`

## Objective

Prove that a separate package consumer can author public Burst nodes and that native Immediate, Budgeted, and BatchedJobsSameFrame reproduce the complete P1 observable semantics.

## Depends on

- `P2-019`.

## Required reading

- `Documentation~/specifications/behavior-case-v1.md`
- `Documentation~/specifications/burst-node-abi-v1.md`
- `Planning~/Evidence/P1-GATE/contract-checklist.md`

## Allowed changes

- `Tests/BehaviorCases/Adapters/Native/`
- `Tests/Integration/NativeRuntime/`
- `Tests/Integration/BurstNodes/`
- `Tests/Fixtures/P2/BurstNodes/`
- `Samples~/BurstNodes/`
- `Documentation~/burst-node-authoring.md`
- `Planning~/Evidence/P2-020/`

## Forbidden changes

- Modification or weakening of P1 behavior-case inputs/expectations.
- Internal runtime access from the consumer sample, managed fallback, or platform/performance claims.

## Deliverables

- Backend-neutral native behavior-case adapter, mechanically discovered P1 case matrix, and public-only custom Condition/Action sample.

## Acceptance criteria

- Every applicable P1 case matches reference for progress/root, blackboard values+versions, ordered commands, diagnostics, complete semantic trace, active operation/node counts, and defined step metrics.
- Deterministic budget partitions and jobs batch/worker partitions preserve results.
- Consumer assembly uses only public API and covers typed read condition, typed write action, Running/abort, and command/async when included by ABI v1.
- Invalid consumer declarations fail through analyzers and never receive a Burst binding.
- Evidence records exact case inventory, policies, toolchain, source revision, and matrix result.

## Required verification

```text
all P1 behavior cases: reference vs native policies
clean UPM consumer compile
custom-node golden and negative analyzer fixture
deterministic repeat/partition matrix
git diff --check
```

## Handoff notes

- This card establishes semantic equivalence, not zero-GC, throughput, or platform support.
