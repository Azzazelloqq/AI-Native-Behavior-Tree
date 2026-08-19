# Phase 3 claims inventory

Prepared 2026-08-19 for the `P3-013` review, against candidate commit
`4700b22e4a17de5d8c118c5d22dfb271a04177fc`. Every supported claim below already
has committed evidence.

## Supported claims

- `UnityEditor.Experimental.GraphView` was selected over Unity Graph Toolkit on
  measured evidence (serialization control, extensibility, testability), not
  assumption (`P3-001`, `P3-014`, `AIBT-012`).
- `*.aibt.layout.json` is a fully AIBT-owned, versioned, canonical format
  (`editor-layout-v1.md`), separate from the semantic `*.aibt.json` and from
  local view state.
- The read-only graph adapter (`P3-003`) never mutates the semantic document,
  on disk or in memory, when opening it for display.
- Auto-layout (`P3-004`) is deterministic: identical input produces
  byte-identical output, verified against golden fixtures and by re-running
  the algorithm.
- Manual organization (pin, group, note, reroute) and its persistence
  (`P3-005`) round-trip losslessly through canonical JSON.
- Every semantic edit (`P3-006`) is gated by the real `ReferenceCompiler`/
  `TreeValidator` pipeline -- there is no separate, weaker in-editor
  validation path.
- Layout/organization actions and auto-layout never change the compiled
  program's content hash; a genuine semantic edit does (`P3-007`), proven as
  an automated regression test, not a review convention.
- Every diagnostic the Authoring validation pipeline can produce resolves to
  a stable Document/Node/Field graph location (`P3-008`).
- In-editor preview stepping cannot drift from the accepted Phase 1 managed
  reference oracle: a step-sequence-and-terminal-status parity proof exists
  against a raw `ReferenceExecutionMachine` built the same way the headless
  behavior-case runner is (`P3-009`).
- The native execution debugger is strictly a reader: attaching/detaching
  produces no measurable managed-allocation change to the acquire/schedule/
  complete/release sequence, and detaching mid-run leaves native output
  byte-for-byte identical to running without a debugger (`P3-010`).
- Scrubbing the trace timeline to a past step reproduces exactly the
  active-node state that step actually produced, verified against an
  independently hand-replayed oracle over the same raw channel records, not
  the model's own logic circularly (`P3-011`); the view explicitly reports
  "channel full/events dropped" rather than silently truncating when the
  bounded channel really overflows (forced via the channel's own unmodified
  eviction logic, not a synthetic hook).
- Large-graph editor operations (render, auto-layout, reposition, semantic
  add, pan/zoom) were measured at 240 (matching `P3-001`'s spike scale), 500,
  1000, and 2000 nodes, both headless and via one live interactive-Editor
  session (`P3-012`); individual per-operation costs stay under 70ms at every
  scale, while full-view render/re-render/load is explicitly reported as
  degraded at 1000 and 2000 nodes, not silently passed.
- Unity `6000.5.8f1` compiles `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor`
  as a detached UPM installation (a fresh project referencing only the
  package and its declared dependencies) and passes 953 EditMode tests with
  0 failed and 0 skipped.
- `AIBT.Runtime` and `AIBT.Authoring` reference neither `UnityEditor`, an MCP
  assembly, an LLM-provider assembly, nor `Unity.Entities`; `AIBT.Editor`
  depends on `Authoring`/`Runtime` only, never the reverse
  (`assembly-dependencies.json`).
- The public surface of `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor` at
  this commit is 382 types and 1994 members (`public-api.txt`/`.sha256`).

## Claims intentionally not made

- Any performance default, regression threshold, or "supported graph size."
  `P3-012` records measurements only; Phase 4 owns calibrated defaults.
- That `Editor/Graph/`'s live `BehaviorTreeGraphWindow`/`BehaviorTreeNode` is
  wired to any of `P3-004`/`P3-005`/`P3-006`/`P3-009`/`P3-010`/`P3-011`'s
  functionality. Each built and independently tested its own API/UI layer
  without touching `Editor/Graph/`'s existing files; wiring them together into
  one live authoring surface is real, disclosed future work, not silently
  done or silently skipped.
- That a debugger can attach to a real, running Play-mode game. No production
  Play-mode host component exists anywhere in AIBT yet; `P3-010`/`P3-011` are
  scoped to self-driven channels a caller (today, a test) constructs and
  hands over.
- That preview/debugger/trace-view can execute arbitrary project-authored
  leaf node behavior. AIBT ships no production per-project leaf-registration
  mechanism yet; these surfaces are fixed to the same Phase 1 fixture/
  built-in node set the headless behavior-case runner already exercises.
- Standalone-Player debugger attachment (`P3-010`'s own explicit deferral).
- Stable public API compatibility beyond the recorded experimental `0.1.0`
  baseline.
- Anything about Phase 1/Phase 2's own native runtime, scheduler, or platform
  claims beyond what `P2-GATE` already recorded -- this gate does not
  re-litigate the accepted Phase 2 gate.
