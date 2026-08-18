# Phase 3 inputs

Prepared 2026-08-18 for the `P2-025` review. Phase 3 owns the visual editor,
layout, and debugger. It consumes the Phase 1 and Phase 2 contracts and may not
redefine them.

## What Phase 3 inherits

- One canonical semantic document, `*.aibt.json`, with a deterministic canonical
  writer and stable structured diagnostics. The editor is a client of that model,
  never a second source of truth.
- A versioned node registry with manifests, so the palette, inspector, and
  validation surfaces are generated from the same contracts an AI author sees.
- A deterministic compiler and an immutable compiled program, so an editor preview
  and a runtime execution of the same document agree.
- Two execution backends behind one semantics: the managed reference oracle for
  stepping, breakpoints, and explanation, and the native executor for realistic
  runtime debugging.
- Native trace and diagnostic channels that are already bounded, so the debugger
  reads a fixed-capacity stream rather than allocating per event.

## Required before implementation

1. Resolve `OQ-005` with a dedicated graph-framework spike and an accepted ADR.
   Unity Graph Toolkit is available in the host project but experimental; evaluate
   serialization control, large-graph performance, extensibility, testability, and
   package risk before taking the dependency.
2. Specify `*.aibt.layout.json` before any editing surface exists, including
   deterministic auto-layout, pinning, groups, comments, sticky notes, reroutes,
   and the split between shared layout and ignored local view state.
3. Prove that a layout-only edit produces no change in the compiled program, as an
   automated test rather than a review convention.
4. Define how the debugger attaches to a native execution without perturbing it,
   and what it is allowed to read from the trace channel.
5. Decide how editor previews reuse the reference oracle so stepping semantics
   cannot drift from native execution.

## Constraints Phase 3 must not violate

- `Runtime` gains no `UnityEditor`, MCP, or LLM dependency.
- Node coordinates, colors, groups, and comments never influence semantics.
- Editor code depends on `Authoring`, never the reverse.
- Editor convenience does not justify relaxing a normative specification or a
  canonical JSON rule.
