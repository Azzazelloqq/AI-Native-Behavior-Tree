# P7-005 — Migration-tooling design decision

Status: `Done`

## Objective

Decide, on paper, what "migration tooling" (`Documentation~/roadmap.md`'s Phase 7 scope,
`Documentation~/scope.md`'s "Versioned node contracts and data migrations" and "migration guides")
concretely means and requires, before building it. `MCP/Documentation/McpMigrationsDocumentGenerator.cs`
already emits a "versioned migrations stub" as part of `P6-011`'s generated documentation, but it is
explicitly a stub — no production code anywhere reads an old node-contract version and produces or
assists a real upgrade of a committed `*.aibt.json`/`*.aibt.layout.json`/behavior-case document when
a node's own `AibtBurstNode`/manifest version increments. This mirrors every other phase's own
pattern of a dedicated spike/decision card before implementation when a roadmap line names a
capability with no existing mechanism spec (`P6-001`/`P6-002`/`P5-001`'s own precedent).

## Depends on

- `P6-011` (generated agent documentation; the existing migrations stub this card's decision
  supersedes or extends).
- `P2-005` (deterministic node generation; node type-version fields already exist and are read by
  the compiler — this card decides how to use that existing version information for real migration,
  not whether to add versioning at all).

## Required reading

- `MCP/Documentation/McpMigrationsDocumentGenerator.cs` (the existing stub — read what it currently
  claims/generates before deciding what to build for real).
- `Documentation~/hot-reload.md`'s compatibility-classification model (`ADR-P5-001`) — a real
  migration mechanism for *authored* documents across a node-contract version bump is a different
  problem from hot-reload's *live-instance* migration, but should reuse identity/versioning
  concepts where they genuinely overlap, not invent a second vocabulary for the same thing.
- `Authoring/Registry/` (the node manifest/registry model versioning already accepted in Phase 1).
- `Documentation~/testing.md`'s "Compiler and format tests" line naming "migrations" as an existing
  test category — confirm what, if anything, already exercises this before assuming a blank slate.

## Allowed changes

- `Spikes~/MigrationToolingDecision/` (new, disposable) — proves the recommended mechanism against a
  real authored document and a real node-version bump.
- `Planning~/Evidence/P7-005/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Authoring/`, `Runtime/`, `MCP/` — this card decides on paper; `P7-006`
  implements it.
- Assuming a specific mechanism (e.g. "an MCP tool") without first checking whether the safer,
  smaller answer ("a documented manual procedure plus the existing diagnostic system already
  flagging a version mismatch clearly enough") already satisfies the roadmap line — per
  `Planning~/DECISION_BOUNDARIES.md`'s "missing detail" rule, do not force machinery beyond what the
  actual gap requires.

## Deliverables

- A decision on scope: does "migration tooling" mean (a) automatic best-effort rewriting of an
  authored document's fields when a node's own contract version changes in a backward-compatible
  way, (b) a diagnostic-driven manual workflow (clear, structured errors plus documented steps, no
  automatic rewrite), or (c) something else — argued from the real gap, not assumed.
- A disposable spike proving the recommended mechanism against a real authored `*.aibt.json` and a
  real node whose contract version was deliberately bumped in the spike's own fixture.
- A proposed ADR recording the decision and exactly what migration scenarios remain unhandled (e.g.
  a breaking, non-backward-compatible field removal) if any.

## Acceptance criteria

- The spike demonstrates the chosen mechanism against a real version bump, not a synthetic
  same-version no-op.
- The ADR states plainly which version-change categories (field add/remove/rename/type-change) the
  mechanism handles and which it explicitly does not.
- No existing accepted contract (compiler behavior, diagnostic codes, manifest schema) is weakened
  to make migration easier — a genuinely unhandled category is disclosed, not forced through.

## Required verification

```text
Verify-Static.ps1
disposable spike: a real authored document run through the recommended mechanism across a real
  node-contract version bump
```

## Handoff notes

- If accepted, `P7-006` applies the ADR to production.

## Outcome

Done. `ADR-P7-005` (`AIBT-036`, Accepted 2026-09-02): migration tooling covers only field-added-with-
default and field-renamed (removal/type-change disclosed as unhandled), via a declarative, ordered
`(NodeTypeId, sourceVersion)` rule registry at the authoring-tooling layer only (never inside the
Burst-compiled node, so ABI v1's ban on node-execution migration callbacks does not apply).
`validate`/`compile` apply migrations to an in-memory copy only — the on-disk document is never
mutated as a side effect. Every applied migration emits a structured, non-blocking `Info`-severity
diagnostic reachable through `explain_diagnostic`, so an MCP-driving AI agent sees exactly what
changed without being blocked — persisting the fix is a separate explicit action (Editor
notification, never gating the MCP path; and an MCP tool), both deferred to `P7-006`. Diff preview
reuses the existing `CanonicalTreeJsonWriter`. Decided through direct discussion with the owner
(this is an AI-first library — diagnostics must be structurally reachable by an agent, not only
readable by a human), not decided unilaterally. A disposable spike
(`Spikes~/MigrationToolingDecision/SpikeMigrationTooling.cs`, run live via Unity MCP, 2/2 passed)
proved the mechanism against a real fixture node type bumped v1→v2 (rename + add-with-default),
compiled through the real `ReferenceCompiler`, plus a negative case (unregistered v2→v3 gap) still
hard-failing through the existing `UnsupportedNodeVersion` diagnostic, unchanged. Full regression
(`AIBT.Editor.Tests`, 376/376) passed with no production file touched. See
`Planning~/Evidence/P7-005/README.md`.
