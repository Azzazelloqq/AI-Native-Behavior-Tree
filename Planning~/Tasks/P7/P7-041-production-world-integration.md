# P7-041 — Consumer-facing production world integration

Status: `Done`

## Objective

Complete the public Unity integration boundary required for a behavior tree to act as a black box:
game code feeds live facts and receives committed typed effects without native-buffer, Job, DOTS or
generated-offset knowledge. This is the prerequisite that lets P7-034 be an honest gameplay sample.

## Depends on

- `P7-030` — one-host lifecycle and ownership.
- `P7-033`/`P7-038` — scheduler and grouped/pipelined dispatch ownership.
- `P7-037` — generated production dispatch and atomic commit.
- `P7-039` — safe live Tree-scope input writes.

## Required reading

- `Documentation~/architecture.md`, `Documentation~/production-host.md` and
  `Documentation~/execution-and-scheduling.md`.
- `ADR-P7-010`, `ADR-P7-033`, `ADR-P7-037`, `ADR-P7-039` and this card's ADR-P7-041.
- `Runtime/Integration/ProductionTreeHost.cs`, generated dispatch adapter/catalog contracts and
  `Runtime/Scheduling/Production/ProductionTreeScheduler.cs`.

## Scope

- A world-controlled scheduler-frame phase bridge, preserving standalone scheduler behavior.
- Typed `Int32`, `Float32` and `Float3` `EffectCommand` delivery after successful commit, including
  grouped-wave attribution, and its source-generated stable descriptor wiring.
- A host-owned bounded effect queue, receiver contract and focused behavior tests.
- Public Unity-facing documentation and an implementation plan/evidence under this task.

## Forbidden changes

- No raw native command/payload/workspace API; no reflection, catalog discovery or global registry.
- No node-side Unity API, managed fallback in Burst work, second native tree state or one Job per
  tree.
- No scheduler-policy/default/budget/due-order behavior change, P7-040 work, generic compiled-tree
  asset pipeline, registered/other structured effect values or async-operation bridge.

## Acceptance criteria

- A world bridge establishes the documented safe frame order for immediate and pipelined work;
  in-flight pipelined waves use a consistent snapshot and receive newer facts at the next idle
  barrier.
- Existing direct host/scheduler use remains behaviorally unchanged when no bridge is bound.
- A real immediate or grouped generated `EffectCommand` reaches a public typed receiver exactly
  once and only after its native transaction commits; failed/rejected callbacks publish none.
- Effect order is deterministic; queue exhaustion is structured and visible, never a drop.
- Input writes occur only while the host is idle; no P7-039 safety rule is weakened.
- Warmed integration paths allocate no managed memory, and native teardown leaves no diagnostics.

## Required verification

```text
Verify-Static.ps1
focused immediate/pipelined phase, typed-effect, negative-commit, overflow and allocation tests
Run-UnityTests.ps1 -Mode EditMode -Scope Full
Unity MCP Play-mode proof with a world, live host and debugger
git diff --check
```

## Handoff

P7-034 depends on this card. It must use the public bridge, not an internal adapter shortcut.

## Outcome

Added `ProductionTreeWorld`, typed committed `ProductionTreeEffect` delivery and a bounded host-owned
effect queue. Generated catalogs expose stable `EffectCommand` descriptors while keeping command
records, native payloads, offsets and grouped workspace details private. The scheduler invokes the
world only at its existing frame boundaries, so immediate work receives current staged facts and
pipelined work retains its admitted snapshot until the next idle barrier.

`ProductionTreeWorldEffectTests` passed 6/6, covering committed exactly-once delivery, descriptor
identity/rejection, group attribution, immediate and pipelined frame ordering, and warmed drain
allocation. Unity MCP also proved a live world/host and debugger path. `Verify-Static.ps1` passes.
The full host-project EditMode run reported 1,806 total / 1,801 passed / 5 failed: two
`GeneratedArtifactContractTests` fail because `PackageInfo.FindForAssembly` returns null for the
embedded `Assets/AIBT` test assembly, two documentation failures were fixed by regenerating the
runtime API and then the full `AIBT.Editor.Tests` assembly passed 436/436, and one unrelated
`LocalSaveSystem.Tests.SaveStore_AutoSave_WritesToDisk` failed (expected 9, got 0). The remaining
three are host/import-context baseline failures outside this card; no test was weakened or changed
to mask them. A final post-refresh retry completed all 1,806 tests and reported exactly those same
three failures (its MCP payload omitted pass/skip totals); no P7-041 test failed.
