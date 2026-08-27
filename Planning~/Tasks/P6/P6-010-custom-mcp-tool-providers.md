# P6-010 — Custom MCP tool provider registration and permission model

Status: `Draft`

## Objective

Implement `Documentation~/ai-and-mcp.md`'s "Custom MCP tools" contract: a
mechanism for a consuming project's own assembly to register a high-level,
project-specific tool (e.g. "create a combat subtree") declaring stable
name, description, JSON input/output schemas, permissions, side effects,
cancellation behavior, dry-run support, and owning assembly, enforced
through `P6-005`'s permission mechanism unchanged.

## Depends on

- `P6-001` (accepted ADR; a custom tool provider must satisfy the same
  assembly-reference and permission-taxonomy decision as built-in tools).
- `P6-005` (MCP server host and permission enforcement).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Custom MCP tools" section — including
  the explicit prohibition: "Do not expose every behavior node as a
  separate MCP tool. Generic semantic operations consume the node
  registry; custom tools represent meaningful project workflows."
- `Documentation~/architecture.md`'s "Dependency direction" — a
  project-owned tool provider assembly must be discoverable without AIBT
  itself referencing that project's assembly (the dependency points the
  other way).
- `P6-005`'s permission-enforcement mechanism (this card's registered
  tools go through the identical check, not a parallel one).

## Allowed changes

- The MCP assembly's provider-registration module (location per `P6-001`'s
  ADR).
- A minimal sample custom-tool-provider project or fixture under
  `Tests/Editor/Mcp/CustomTools/` demonstrating registration end-to-end.
- `Planning~/Evidence/P6-010/`.

## Forbidden changes

- Any mechanism requiring `AIBT.MCP`/`AIBT.Authoring`/`AIBT.Runtime` to
  reference a specific consuming project's assembly — discovery must be
  inversion-of-control (attribute/interface implemented by the consumer,
  discovered by the host), never a hardcoded reference.
- Assembly scanning in player/runtime code, consistent with `P1-004`'s own
  "no assembly scanning occurs in player/runtime code" acceptance
  criterion — discovery happens in the MCP/Editor-only host process.
- Granting a custom tool broader access than its declared permission
  category, regardless of what its own code attempts.

## Deliverables

- A `ICustomMcpToolProvider`-shaped (or equivalent, per `P6-001`'s ADR)
  registration contract: name, description, JSON Schema input/output,
  declared permission category, side-effect declaration, cancellation
  support, dry-run support, owning assembly.
- A discovery mechanism that finds registered providers in the current
  Unity project without AIBT referencing them directly.
- Enforcement proof that a custom tool's declared permission category is
  checked by the same `P6-005` mechanism built-in tools use, not a second
  path.
- A minimal real sample: one custom tool provider registered, discovered,
  invoked through a real MCP client, both an allowed and a
  permission-rejected call.

## Acceptance criteria

- The sample custom tool is discovered without any AIBT assembly
  referencing the sample's assembly.
- A custom tool declared with `read` permission is rejected when it
  attempts a semantic-edit-shaped operation internally, proven by a
  negative test, not just by trusting its declaration.
- Removing the sample provider assembly leaves the MCP server functioning
  normally (no hard dependency on any specific custom provider existing).
- Cancellation and dry-run declarations are honored: a custom tool
  declaring dry-run support is called in dry-run mode and the harness
  verifies its own no-persistence contract is upheld (best-effort, since
  the tool's internal implementation is project-owned — document this
  boundary explicitly rather than overclaiming enforcement of code the
  host cannot see into).

## Required verification

```text
real MCP client: discover, invoke (allowed), invoke (permission-rejected)
  sample custom tool
provider-assembly-removed regression check
Verify-Static.ps1
```

## Handoff notes

- `P6-011` documents this registration contract for project authors as its
  own recipe, since it is the one Phase 6 surface meant to be used by
  someone other than an AI agent driving AIBT's own built-in tools.
