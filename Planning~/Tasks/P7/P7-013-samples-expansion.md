# P7-013 — Samples expansion

Status: `Draft`

## Objective

Add the additional samples `Documentation~/roadmap.md`'s Phase 7 scope and
`Documentation~/scope.md`'s "Samples, recipes, anti-patterns" line call for, beyond the two that
exist today (`Samples~/BurstNodes`, `Samples~/SemanticSlice`). Prioritize samples that demonstrate a
real, already-accepted capability with no working example yet: a custom MCP tool provider
(`P6-010`'s own extension point is proven but has no shipped sample, explicitly suggested as
in-scope by `Planning~/Evidence/P6-GATE/phase7-inputs.md`) and a fuller example project combining
scheduling policy selection, the editor workflow, and hot reload in one place, since the two
existing samples are narrowly scoped (one Burst-node ABI conformance sample, one semantic-slice
golden path).

## Depends on

- `P6-010` (custom MCP tool provider registration; the sample this card adds proves the real,
  already-accepted extension point).
- `P5-008` (editor hot-reload workflow; the fuller example sample exercises this).

## Required reading

- `Samples~/BurstNodes/README.md` and `Samples~/SemanticSlice/README.md` (the existing house style
  for a sample's own scope/README, to match rather than invent a new format).
- `Planning~/Evidence/P6-010/` (the sample custom-tool-provider fixture assembly already used for
  testing — reuse its proven shape rather than designing a new one from scratch).
- `Documentation~/ai-and-mcp.md`'s "Custom MCP tools" section.

## Allowed changes

- `Samples~/CustomMcpToolProvider/` (new).
- `Samples~/FullExample/` (new, or a more specific name chosen during the card — a project
  demonstrating multiple node kinds, an explicit scheduling-policy choice, and a hot-reload workflow
  pass).
- `Planning~/Evidence/P7-013/`.

## Forbidden changes

- Any change to `Samples~/BurstNodes/` or `Samples~/SemanticSlice/`'s own existing content beyond
  what a genuine shared-fixture reuse requires (disclose if any is needed).
- Introducing a new public API surface solely to make a sample look better — a sample demonstrates
  existing, already-accepted capability; it does not justify a new capability.

## Deliverables

- A real, working custom-MCP-tool-provider sample: a separate sample assembly implementing
  `ICustomMcpToolProvider`, discoverable and callable through a real running `MCP~/Server/` session,
  with a README walking through registering and calling it.
- A fuller example sample combining: at least one custom Burst node, an explicit scheduling-policy
  choice (not `Auto`, per `P4-006`'s own honest finding that `Auto` underperforms today), and one
  full hot-reload workflow pass through `P5-008`'s own Editor window.

## Acceptance criteria

- Both samples compile cleanly in a detached UPM harness (mirroring every gate's own clean-checkout
  discipline), not only inside the host `Modules` project.
- The custom-tool-provider sample is proven live against the real permanent `MCP~/Server/` via the
  official Inspector CLI, matching `P6-010`'s own verification bar.
- The fuller example sample's hot-reload pass is proven live via Unity MCP against the real open
  Editor, matching `P5-008`'s own verification bar.

## Required verification

```text
Verify-Static.ps1
detached UPM harness compile check for both new samples
live end-to-end verification: custom-tool-provider sample via the real MCP~/Server/ and Inspector CLI
live interactive verification: fuller example's hot-reload pass via Unity MCP against the real open Editor
```

## Handoff notes

- None.
