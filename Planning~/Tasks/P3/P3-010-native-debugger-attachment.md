# P3-010 — Native execution debugger attachment

Status: `Done`

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

## Outcome

- Research before implementation found that **no production Play-mode host component exists
  anywhere in AIBT** — nothing instantiates or drives a native lifecycle machine during Play mode,
  and no production code wires a `NativeTraceChannelOwnerV1` to a live pass at all (the only
  non-test writer caller is a synthetic single-record Burst-compile proof). The card's premise of
  attaching to "a running native executor instance (in-Editor Play mode first)" therefore had
  nothing to attach to. Escalated to the owner (`AskUserQuestion`, 2026-08-19) per
  `DECISION_BOUNDARIES.md` rather than silently building a Play-mode host (new production
  architecture, outside this card's scope) or silently reinterpreting the card. Accepted answer:
  narrow scope to proving the attach/detach/read protocol against a self-driven native pass —
  mirroring how P3-009's Preview owns its own reference-executor instance — with the gap disclosed,
  not papered over.
- `Editor/Debugger/NativeExecutionDebuggerSession.cs`: `Attach`/`Detach`/`TryReadTrace` over a
  caller-owned `NativeTraceChannelOwnerV1`. No new assembly-boundary facade was needed (unlike
  P3-009) — every native trace type is already `public` in `AIBT.Runtime`; `Editor/AIBT.Editor.asmdef`
  and `Tests/Editor/AIBT.Editor.Tests.asmdef` gained a `Unity.Collections`/`Unity.Burst` reference
  each, since both use explicit (`overrideReferences: true`/no prior native reference) reference
  lists.
- 5/5 automated tests passing, including a real `[BurstCompile]` job writing through the real
  unmodified `NativeTraceWriterV1`, an allocation-neutral proof isolating only the acquire/schedule/
  complete/release sequence (matching how `NativeExecutionAllocationTests` isolates its own measured
  calls), and a byte-for-byte detach-mid-run-is-unaffected proof. Live-verified interactively in the
  running `6000.5.8f1` Editor via Unity MCP (`execute_code`): created a real channel, wrote a real
  record, attached, and read it back correctly, with no console errors.
- Full evidence: `Planning~/Evidence/P3-010/README.md`, `verification-results.json`.
