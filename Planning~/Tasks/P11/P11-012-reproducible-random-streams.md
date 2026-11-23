# P11-012 — Reproducible random streams (ADR-P11-008 → Accepted, AIBT-052)

Status: `In progress` — committed on `feature/aibt-consumer-workflow`, waiting for the orchestrator's
acceptance run.

## Objective

Implement ADR-P11-008 (review finding F13) with the decisions the orchestrator took on 2026-11-23 under
the owner's delegation, after an independent read:

1. `NativeTreeAgent.RandomStreamId` (serialized `ulong`, Inspector field and property): 0 is automatic
   (today's behaviour), nonzero makes a run's stream identity a function of (id, run index) only. No
   generated GUID, no refusal.
2. A read-only run index: 0 on every enable, +1 for each run that started while the component stays
   enabled; a failed start does not consume one.
3. Additive host API: overloads on `ProductionTreeHost` and `ProductionTreeProgram` taking the seed and
   the stream identity as one value type (identity 0 automatic); the existing overloads keep their
   behaviour and say in their XML docs that a seed alone is not repeatable; `InstanceId` keeps every
   other use.
4. `TreeSimulation` unchanged; the host/container equivalence test may pass an identity instead of
   setting private counters.
5. The derivation keeps its name; `time-and-random-v1.md` names the "stream identity", states the host's
   and the agent's policy and defines the combine function with a test vector (not `+`, not `xor`,
   never 0).
6. Non-goals stated in the ADR and the user guide: update order follows start order and draws follow
   frame time; lockstep is `TreeSimulation`'s job.

First fix the ADR against the code (line references, the id a failed bootstrap consumes, the scheduler
tie-break, the normative text), verifying each fact. Also: an inspector help box, the Native Guard
sample, `native-node-library.md`, the generated API reference, `CHANGELOG.md`, `decisions.md`.

## Depends on

- `P11-008` (the proposed ADR).

## Scope

- `Runtime/Integration/RandomStreamSeeding.cs` (new), `ProductionTreeHost.cs`, `ProductionTreeProgram.cs`,
  `NativeTreeAgent.cs`, `ProductionTreeAsset.cs` (an internal program read for the Editor),
  `GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` (an internal seeding overload).
- `Editor/Graph/NativeTreeAgentInspector.cs`.
- Tests: `Tests/Integration/NativeRuntime/NativeTreeAgentRandomSeedTests*.cs`,
  `RandomStreamSeedingTests.cs`, `Tests/Integration/Simulation/HostRandomSeedingTests.cs`,
  `Tests/Editor/Graph/NativeTreeAgentRandomStreamHintTests.cs`.
- `Documentation~/specifications/time-and-random-v1.md`, `Documentation~/decisions/ADR-P11-008-*.md`,
  `Documentation~/decisions.md`, `Documentation~/native-node-library.md`, `Documentation~/production-host.md`,
  `Documentation~/generated/api-reference-runtime.md` (through its generator), `Samples~/NativeGuard/`,
  `CHANGELOG.md`, `Planning~/`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  `package.json`, pins and fixtures, existing public signatures (only additions), `TreeSimulation`, the
  derivation's name and mix.

## Acceptance criteria

- Tests first (failing before), through the public API: two agents with ids, three runs each, recorded;
  destroyed, unrelated hosts and a failed bootstrap between, started in the other order: every (id, run)
  pair draws the same; disable/enable repeats run 0; `Restart()`; a change of `Tree`; two agents with one
  id and seed draw the same; id 0 stays distinct per agent; every scheduling policy the seed tests cover;
  the existing derivation tests stay valid.
- One commit per logical change; the touched tests green with counts; the Console free of new errors;
  the submodule checkout clean; `Tools~/Verification/Verify-Static.ps1` passes.
- `Planning~/Evidence/P11-012/README.md` with raw files in `C:\UnityProjects\_aibt_acceptance\P11-012\`.

## Evidence

`Planning~/Evidence/P11-012/README.md`.
