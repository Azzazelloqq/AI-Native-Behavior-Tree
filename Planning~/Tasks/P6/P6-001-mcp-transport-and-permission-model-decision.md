# P6-001 — MCP transport, hosting, and permission-model decision

Status: `Draft`

## Objective

Decide, on real evidence rather than assumption, how AIBT exposes an MCP
server from this Unity C# package: SDK/library choice, process/hosting
model, transport, the new assembly's exact position in the dependency
graph, and the conceptual shape of the permission model every later MCP
tool card declares against. This card decides the model; it does not ship
a working server (`P6-005` does).

## Depends on

- `P5-010` (Phase 5 integration gate; Phase 6 entry per `MASTER_PLAN.md`).

## Required reading

- `Documentation~/ai-and-mcp.md` (entire document — the normative contract
  this decision must satisfy).
- `Documentation~/architecture.md`'s "Layers" (`### MCP server`), "Core data
  ownership," and "Dependency direction" sections — note the existing
  diagram already places `MCP` as a sibling of `CodeGen`/`Editor`, not a
  child of `Runtime`, and states MCP "is not required in player builds."
- `Planning~/OPEN_QUESTIONS.md`'s closed-and-must-not-reopen list: "MCP is
  optional and not a runtime dependency."
- `Planning~/Evidence/P5-GATE/phase6-inputs.md` — the concrete shape
  (`HotReloadPreviewDriver`) later MCP tools will wrap, and the
  reference-executor-only disclosure obligation this decision's assembly
  boundary must not make harder to honor.
- `Planning~/DECISION_BOUNDARIES.md` — "public or cross-assembly API shape"
  and "new package dependency, assembly reference... platform conditional"
  are both "must escalate" categories this card exists to resolve properly.

## Allowed changes

- `Spikes~/McpTransportModel/` (new, disposable).
- `Planning~/Evidence/P6-001/`.
- One proposed ADR; integration owner applies accepted decision updates to
  `Documentation~/decisions.md` and `Documentation~/ai-and-mcp.md`.

## Forbidden changes

- Production `MCP/`, `Runtime/`, `Authoring/`, or `Editor/` implementation
  of any server, transport, or tool — this card decides the model on paper,
  backed by a disposable spike proving the chosen transport is at least
  constructible end-to-end (one resource + one tool call, real client, real
  response), it does not ship a server.
- Introducing a package dependency (e.g. an MCP C# SDK) into any
  `package.json`/`.asmdef` outside the disposable spike without the
  decision being accepted first.
- Weakening "MCP is optional and not a runtime dependency" or "no lower
  layer may reference a higher layer" to make the transport simpler.

## Deliverables

- A decided SDK/library choice (or justified custom minimal JSON-RPC
  implementation), backed by a real feasibility check against this
  project's exact Unity `6000.5.8f1`/C# toolchain — not a web search alone,
  per this project's own spike discipline.
- A decided process/hosting model: in-Editor-process server started by an
  Editor menu/window (mirroring `P3-009`/`P5-008`'s own-window pattern), an
  external process the Editor launches, or an external process an AI
  client launches that connects back — stated with its consequences for
  "must not be required in player builds" and for whether the server can
  see live Play-mode state.
- A decided transport (stdio, HTTP+SSE, or other) and exactly which real
  MCP client(s) this was checked against.
- A decided new-assembly shape: name, which existing assemblies it may
  reference (`AIBT.Runtime`, `AIBT.Authoring`, `AIBT.Editor` — confirm or
  correct `architecture.md`'s existing diagram rather than silently
  assuming it), and confirmation the reverse never holds.
- A decided permission-model taxonomy (read, semantic edit, layout edit,
  code generation, compilation, test execution, benchmark execution,
  arbitrary project integration, per `ai-and-mcp.md`'s "Safe mutation
  protocol") as a concrete type every later tool card's Deliverables must
  declare against — not implemented enforcement yet (`P6-005` implements
  enforcement), just the shape.
- A disposable spike proving the chosen transport/hosting model end-to-end:
  a real MCP client connects, lists at least one resource, calls at least
  one no-op tool, and receives a structured response.
- A proposed ADR recording the decision and its rationale.

## Acceptance criteria

- The decision states explicitly how a player build remains unaffected
  (no new player-facing assembly reference, no new player-facing
  dependency) — checked, not assumed.
- The decision states explicitly what happens when no MCP client is
  connected (the Editor and any Player must work identically to today).
- The spike used a real MCP client (not a hand-rolled fake) to exercise
  the transport at least once.
- The ADR states exactly what remains unverified (e.g., concurrent
  clients, authentication/remote transport, cross-platform hosting)
  rather than generalizing.

## Required verification

```text
Verify-Static.ps1
disposable spike: real MCP client connects, lists a resource, calls a tool
```

## Handoff notes

- `P6-003` through `P6-012` are blocked on this card's ADR being accepted,
  not merely on this card being `Done` — mirrors how `P3-001` and `P5-001`
  gated every later card in their phases.
- If the natural transport choice cannot actually run inside/alongside the
  installed Unity Editor on this workstation, iterate rather than shipping
  a known-broken decision — the same discipline `P3-001`'s spike applied
  when it rejected Unity Graph Toolkit on real evidence.
