# P7-011 — Native-backend hot reload decision

Status: `Draft`

## Objective

Decide, on paper, how `ADR-P5-001`'s already-accepted construct-fresh-and-selectively-copy hot-reload
model applies to the native execution backend. `P5-004`/`P5-005`/`P5-006` built full restart, subtree
restart, and compatible migration for the reference-executor backend only, explicitly disclosing "the
native backend's own fresh-instance construction, a separate capacity-plan/lease subsystem, remains
open follow-up work" (`P5-004`'s own evidence) — this has been restated, unchanged, through
`P5-GATE`, `P6-GATE`, and this project's own Phase 7 handoff notes. This card is the dedicated
decision cycle that gap has been waiting for.

## Depends on

- `P5-010` (Phase 5 gate; the accepted reference-executor hot-reload model this card extends to the
  native backend, not replaces).

## Required reading

- `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md` (the accepted model:
  reload is never in-place mutation; full/subtree/compatible are one mechanism with a different
  exclusion set, keyed by stable authoring node ID, never compiled index).
- `Planning~/Evidence/P5-004/README.md`'s own disclosed native-backend gap (the exact starting point
  for this card).
- `Runtime/Compiled/Native/NativeProgramImageOwner.cs`, `Runtime/State/Native/NativeInstanceArenaOwner.cs`
  (the fixed-capacity native program/state ownership this card's fresh-instance construction must
  respect — native memory is pre-planned/leased, unlike the reference executor's managed allocation,
  which is exactly why this was disclosed as a "separate capacity-plan/lease subsystem" problem, not
  a mechanical port of the reference-executor implementation).
- `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs` (the native equivalent of
  `ReferenceExecutionMachine`, whose `CaptureNodeState`/`SeedNodeState` accessor pair
  `P5-006`/`P6-018`'s own ADR already added to the reference-executor machine — this card decides
  whether an equivalent pair is buildable here, and if not, why).

## Allowed changes

- `Spikes~/NativeHotReloadModel/` (new, disposable) — proves the recommended design against a real
  compiled native program pair (old/new), mirroring `P5-001`'s own spike-before-ADR methodology.
- `Planning~/Evidence/P7-011/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Runtime/Execution/Native/`, `Runtime/Compiled/Native/`,
  `Runtime/State/Native/`, `Runtime/Scheduling/Native/` — this card decides on paper.
- Reopening `ADR-P5-001`'s own accepted model (construct-fresh-and-selectively-copy, keyed by stable
  node ID) — this card applies it to a new backend, it does not redesign the model itself.
- Introducing any live-adapting scheduling state to solve a native-reload convenience problem —
  `OQ-006` already rejected runtime autotuning; this restates that boundary explicitly for whoever
  implements this card's own follow-up.

## Deliverables

- A decision on native fresh-instance construction: how a new `NativeProgramImageOwner`/
  `NativeInstanceArenaOwner` pair is planned and leased for a reloaded program, given the backend's
  fixed-capacity, pre-planned nature (unlike the reference executor's own managed allocation).
- A decision on state capture/seeding for the native backend, mirroring `CaptureNodeState`/
  `SeedNodeState`'s own shape if buildable, or a disclosed, reasoned explanation of what differs and
  why a direct port does not apply.
- A decision on whether native migration, like the reference-executor's own `ADR-P5-001`
  implementation addendum, is scoped to an idle old instance only, or whether the native backend's
  own execution model allows (or requires) a different boundary — argued from real code, not
  assumed identical to the reference-executor finding.
- A disposable spike proving the recommended design against a real compiled native program pair,
  live via Unity MCP.
- A proposed ADR.

## Acceptance criteria

- The spike proves at least full restart for a native-backend instance, live, against a real
  `NativeLifecycleMachineV1` pair — not merely designed on paper.
- The ADR states plainly which of full restart / subtree restart / compatible migration are
  proven for the native backend by this card's own spike, and which remain follow-up.
- No accepted Phase 4 scheduler contract (policy semantics, work-estimator behavior) is reopened —
  `P5-007`'s own already-made estimator reset-vs-carry-over decision is inherited unchanged unless
  this card finds a concrete, disclosed reason it does not hold for the native backend.

## Required verification

```text
Verify-Static.ps1
disposable spike: real compiled native program pair, live via Unity MCP, at least full restart proven
```

## Handoff notes

- If accepted, `P7-012` applies the ADR to production and unblocks `P5-007`'s own remaining,
  currently-blocked acceptance criteria (golden-equivalence re-run, batch isolation, `Auto`
  determinism, all for a hot-reloaded native instance) for the first time.
