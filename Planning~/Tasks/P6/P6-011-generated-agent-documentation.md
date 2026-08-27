# P6-011 — Generated agent documentation

Status: `Draft`

## Objective

Generate `Documentation~/ai-and-mcp.md`'s "Agent documentation" deliverables
— a short workflow guide, the node catalog, recipes, anti-patterns, good
and bad examples, and versioned migrations — from the same metadata
`P6-003`'s query layer already exposes, per the document's explicit
"Duplicated hand-maintained catalogs are forbidden" rule. Optional
`AGENTS.md`/`SKILL.md` adapters may also be generated.

## Depends on

- `P6-003` (node catalog and project manifest query layer — the data
  source).
- `P6-006` (MCP authoring tools — the source for authoring recipes/
  examples, quoted from real tool schemas, not paraphrased).
- `P6-009` (node development tools — the source for the "add a custom
  node" recipe).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Agent documentation" section.
- `Authoring/Discovery/` (`P6-003`'s query layer — the only data source
  this generator may read).
- The MCP tool schemas registered by `P6-006`/`P6-007`/`P6-008`/`P6-009`
  (the source for recipes' exact tool-call shapes).

## Allowed changes

- `Authoring/Documentation/` (new) or the generator location `P6-001`'s
  ADR names, plus its generated-output destination (e.g.
  `Documentation~/generated/`).
- `Tests/Editor/Documentation/` (new).
- `Planning~/Evidence/P6-011/`.

## Forbidden changes

- Hand-writing catalog content that duplicates `P6-003`'s registry-sourced
  data — every node entry in the generated catalog must trace to a real
  registry record.
- Any claim in generated guidance that a capability exists when it does
  not (e.g. must not claim native-backend hot reload, must not claim
  Play-mode debugger attach, per `Planning~/Evidence/P5-GATE/
  known-limitations.md` and `P6-008`'s own disclosure) — generated text is
  still subject to the same no-overclaiming discipline as hand-written
  evidence.

## Deliverables

- A generated node-catalog document sourced from `P6-003`.
- A generated short workflow guide (connect, discover, author, verify,
  apply) reflecting the actual registered MCP tools, not an idealized set.
- Generated recipes and anti-patterns for the operations Phase 6 actually
  built (create/edit a tree, generate a custom node, run a benchmark,
  inspect a trace), each recipe using a real, runnable tool-call sequence.
- A generated migrations document stub (versioned; may be near-empty at
  `0.x`, but the format must be correct so later phases append to it
  rather than inventing a new format).
- Regeneration is idempotent and deterministic: running it twice with no
  underlying registry/tool change produces byte-identical output.

## Acceptance criteria

- Every node in the generated catalog matches `P6-003`'s query-layer output
  for that node, field for field.
- A recipe's tool-call sequence, when actually executed through a real MCP
  client, succeeds and produces the outcome the recipe describes.
- Regenerating after adding one fixture node changes only that node's
  entry, proven by a diff.
- No generated document contains a machine path, timestamp, or
  locale-dependent text, consistent with `P2-004`'s own generated-output
  determinism requirement.

## Required verification

```text
generation determinism/idempotency test (rerun, byte-identical)
recipe-execution proof: each generated recipe run against a real MCP client
Verify-Static.ps1
git diff --check
```

## Handoff notes

- `P6-012` (the Phase 6 gate) checks generated documentation against
  `claims-inventory.md`-style scrutiny, the same way `P5-010` checked
  `README.md`/`CHANGELOG.md` updates.
