# P3-010 — Native execution debugger attachment

Status: `Draft`

## Objective

Define and implement how the editor debugger attaches to a running native (Burst) executor without perturbing it, reading only the already-bounded native trace/diagnostic channel (`phase3-inputs.md` required item 4).

## Depends on

- `P3-006`.

## Required reading

- `Documentation~/specifications/execution-semantics-v1.md`.
- Native trace/diagnostic channel contracts under `Documentation~/specifications/` (bounded-capacity, fixed-capacity storage rules already normative for Phase 2).
- `Planning~/Evidence/P2-GATE/contract-checklist.md` ("Bounded native diagnostics and trace channels").

## Allowed changes

- `Assets/AIBT/Editor/Debugger/` (new).
- `Tests/Editor/Debugger/` fixtures.
- `Planning~/Evidence/P3-010/`.

## Forbidden changes

- Any change to the native trace/diagnostic channel's capacity, allocation behavior, or write path — the debugger is strictly a reader.
- A synchronous stall of the native executor to service the debugger; attachment must not change tick timing in a way that alters observable scheduling.

## Deliverables

- A defined attach/detach protocol: how the editor locates a running native executor instance (in-Editor Play mode first; standalone Player attachment explicitly out of scope unless separately accepted) and begins reading its trace channel.
- Read-only access to active node, step history, and diagnostic events from the existing bounded channel.

## Acceptance criteria

- Attaching and detaching the debugger produces no measurable change in per-tick allocation or the zero-GC claim already established for Phase 2's initialized native paths.
- The debugger never blocks or slows native execution waiting for UI to consume trace events; a full channel drops or overwrites per its existing bounded-capacity contract, not by the debugger's design.
- Detaching mid-run leaves the native executor in an unaffected state, verified by comparing its output with and without a debugger attached.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <debugger attachment fixture>
allocation comparison with and without an attached debugger
```

## Handoff notes

- `P3-011` (trace views) consumes this card's channel-reading connection; keep the read API stable once accepted.
- Standalone-Player attachment is explicitly deferred; if a future task takes it on, it needs its own accepted decision, not a silent extension of this card's scope.
