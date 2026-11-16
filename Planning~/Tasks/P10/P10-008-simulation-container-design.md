# P10-008 — Design of a GameObject-free simulation container (stage G0)

Status: `Review`

## Objective

An ADR and a game-team review document that show how AIBT meets a game's ten requirements for
agents without GameObjects, stepped by the game's own tick. The documents give the status of each
requirement with code evidence, the public API for items 1–7, the ABI and contract impact, the
owner decisions with options, the proof plan and the stage plan G1–G5. Design only.

## Owner decisions for this card

- The owner approved the re-plan: G0 (this card: ADR + API agreement), then G1–G5, then features
  F1–F5.

## Depends on

- P10-004 (JobsAgentFrame), P10-005 (measurements used for the estimate), P10-007 (threshold).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Planning~/DECISION_BOUNDARIES.md`,
  `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md`, `Documentation~/performance.md`,
  `Planning~/Evidence/P10-005/README.md`, `Documentation~/specifications/time-and-random-v1.md`,
  `update-phases-v1.md`, `native-runtime-v1.md`, `burst-node-abi-v1.md` (read only).

## Scope

- `Documentation~/decisions/ADR-P10-008-simulation-container.md` (new),
  `Documentation~/proposals/simulation-container-review.md` (new), this card, `work-items.json`,
  `CHANGELOG.md`.

## Forbidden changes

- Runtime, generator, test or benchmark code; normative specs; ABI v2; package version.
- Pushing; committing the parent repository.

## Deliverables

- The ADR, which covers: context; the ten requirements with status and file:line evidence; the
  decision; the alternatives (manual step on the scheduler/host, hosts as plain classes, separate
  container (chosen), new context API with raw pointers, bulk blackboard writes); consequences;
  what stays untouched; migration thoughts for `NativeTreeAgent`; the API sketch with its rules; ABI
  and contract impact; owner decisions D1–D9; proof; the estimate; the stage plan; questions; and
  what could not be established.
- The review document: status table, API sketch, proof plan and questions, consistent with the ADR.

## Acceptance criteria

- Every status claim cites code. Background facts that turned out wrong are named in the handoff.
- Additive and breaking impacts are separated. No change to ABI v2 is proposed for items 1–7.
- The estimate shows its derivation and is labelled as an estimate.
- `Verify-Static.ps1` passes.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools~/Verification/Verify-Static.ps1
```

## Handoff notes

- Background facts corrected by the code survey:
  - Inputs do not need a new ABI. `SnapshotReadHandle<T>` exists and is readable in Enter, Tick and
    Observer, and snapshot payloads are raw `T` bytes.
  - Struct commands already go through `CommandHandle<T>` for registered value types. Only delivery
    is limited to Int32/Float32/Float3.
  - `ProductionTreeProgram` already is the shareable, GameObject-free program, so no new
    `TreeProgram` type is needed.
  - Root completion stops a host (`_ready = false`), so the container needs a restart policy.
  - `TreeInstanceId` seeds random streams by spec, so the process-wide counter breaks determinism
    across runs.
- Open item for the owner, outside this card: production random streams appear to be unseeded
  (zeroed arrays); see the ADR section "Not established from the code".
