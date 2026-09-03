# ADR P7-005: migration-tooling design decision

- Status: Accepted 2026-09-02
- Date: 2026-09-02
- Decision ID: AIBT-036

## Context

`Documentation~/data-formats.md:52-54` (normative, already accepted) requires: "Each persisted
format and node type is independently versioned. Migrations are deterministic, testable, ordered,
and produce a previewable diff. Unsupported future versions fail with structured diagnostics; they
are never loaded on a best-effort basis." No mechanism for this exists today for node types.

The real gap, established before deciding anything:

- **Today's behavior is a hard equality check with no compatibility path.**
  `Authoring/Validation/TreeValidator.cs:487` — `if (node.TypeVersion <= 0 || (uint)node.TypeVersion
  != manifest.Version)` — emits `TreeValidationDiagnosticCodes.UnsupportedNodeVersion` (`AIBT2022`)
  for *any* version mismatch, with no distinction between a breaking and a mechanically-safe change.
  Any node-type version bump instantly invalidates every existing authored `.aibt.json` referencing
  it, with only a diagnostic pointing at the location — no automated or documented recovery path.
- **Zero migration execution machinery exists anywhere for node types**, confirmed by repo-wide
  grep. The closest precedent, `MigrationSourceVersion`/`MigrationContractId` on
  `RegisteredUnmanagedTypeDescriptor` (`Runtime/Blackboard/Types/BlackboardTypeDescriptor.cs:151-202`),
  is declared metadata for *blackboard value types* with no consumer anywhere in the codebase (only
  contributes to a content hash, `Runtime/Blackboard/Storage/RegisteredBlackboardBindings.cs:89-95`)
  — a schema placeholder, never wired to real execution, and scoped to a different kind of type
  entirely (blackboard values, not node contracts).
- **ABI v1 forbids custom migration callbacks inside a Burst-compiled node**
  (`burst-node-abi-v1.md:429-430`). Any real mechanism must live at the authoring-tooling layer,
  operating on the authored document before compilation — never inside the node's own execution
  contract.
- **No node type has ever actually been version-bumped** in this project's history (confirmed: no
  `"version": 2` or higher anywhere in any committed fixture, sample, or manifest). This is a
  genuinely greenfield decision with zero real-world precedent to validate scope against, not a
  wire-up of something partially built.
- **This is an AI-first library.** The MCP surface (`P6-005`-`P6-012`) is the primary way an
  external agent drives AIBT, not only a human in the Unity Editor. Whatever migration diagnostic
  this ADR proposes must be structurally reachable the same way every other AIBT diagnostic already
  is (`explain_diagnostic`, `P6-007`), not only human-readable prose in an Editor window.

Discussed directly with the owner before writing this ADR (not decided unilaterally): the owner
confirmed both automatic in-place recovery ("nothing should fall apart after the version bump") and
explicit, structured surfacing of what changed ("tell the user/agent it happened here, worth a
second look") are wanted together, not one instead of the other — and that persisting the fix to
disk must be a separate, explicit action, matching this project's own established pattern
(`SemanticEditTransaction`/`SemanticPatchTransaction`, `P6-004`: no tool mutates persisted state as
a side effect of a read/validate/compile path). The owner also required that any Editor-side UI
surfacing this must never block the MCP/AI-agent path — the in-memory auto-fix and its diagnostics
must already be sufficient for a headless agent to proceed without any human interaction.

## Decision

**Scope: two categories only.** Migration tooling covers exactly two mechanically-safe,
semantically-unambiguous change categories:

1. **Field added with a default value.**
2. **Field renamed** (same type, same semantic meaning, only the JSON key changes).

**Field removal and type-change are explicitly out of scope and remain hard failures** through the
existing `UnsupportedNodeVersion` diagnostic, unchanged — per this card's own acceptance criterion,
a genuinely unhandled category is disclosed, never forced through with a guess. This keeps the
mechanism proportionate to a requirement with zero real precedent, rather than building a general
transform engine (renames, type coercions, structural restructuring, cross-field derivations) for
scenarios that have not yet occurred once.

**Mechanism: a declarative, ordered migration-rule registry, authoring-layer only.** For each
`(NodeTypeId, sourceVersion)` pair, an optional registered rule set (add-with-default / rename
entries) describes how to transform that node's authored `parameters` JSON block from `sourceVersion`
to `sourceVersion + 1`. Rules chain: migrating v1 → v3 applies the v1→v2 rule then the v2→v3 rule,
never a skip-ahead shortcut. This is pure data-in/data-out over already-parsed JSON — it runs in
`Authoring/`, before `ReferenceCompiler` ever sees the document, and never touches the Burst-compiled
node's own execution contract, so ABI v1's ban on node-execution migration callbacks does not apply.
This is the first real execution engine for the shape `RegisteredUnmanagedTypeDescriptor` already
declared but never wired up for blackboard types — the node-contract case reuses the same
`(sourceVersion, contractId)`-keyed shape rather than inventing a second vocabulary, even though it
is a structurally separate registry (node contracts, not blackboard value types).

**When it runs: in-memory only, both the Editor and MCP paths.** `validate`/`compile` detect an
authored node reference at an old-but-migratable version, apply the registered rule chain to an
in-memory copy of the document, and proceed as if the document had been authored at the current
version. **The on-disk `.aibt.json` is never mutated as a side effect of validate or compile** —
matches `SemanticEditTransaction`'s own established "no silent mutation, explicit apply only"
pattern. If no rule chain reaches the current version (a removed/type-changed field, or simply no
rule registered), validation/compilation fails exactly as it does today — `UnsupportedNodeVersion`,
unchanged.

**Diagnostics: structured, non-blocking, MCP-reachable.** Every applied migration produces one
diagnostic per affected node — proposed as a new code in `TreeValidationDiagnosticCatalog`'s own
range (next free slot after `AIBT2041`, i.e. `AIBT2042 MigrationApplied`, exact number confirmed by
whichever card actually adds it, since this ADR does not touch production code), severity `Info`
(`DiagnosticSeverity.Info`, `Runtime/Core/Diagnostics/DiagnosticSeverity.cs` — never `Error`,
matching "this already works, here is what changed" rather than "this failed"). The diagnostic
names: tree ID, node ID, `NodeTypeId`, source version → target version, and the specific field-level
changes applied (e.g. `field 'acceleration' added, default 0.5`; `field 'moveSpeed' renamed to
'speed'`). Reachable through `explain_diagnostic` (`P6-007`) exactly like every other AIBT
diagnostic — an MCP-driving agent sees it structurally in a normal `validate`/`compile` response and
can act on it (apply broadly, ask the user, flag for review) without anything blocking that path.

**Persisting the fix is a separate, explicit action**, deferred entirely to `P7-006`:

- **Editor**: a non-blocking notification (mirroring `Editor/Validation/`'s existing
  diagnostic-summary pattern, `P3-008`, rather than a new UI paradigm) lists documents with
  migratable nodes and what would change, with an explicit action to write the migrated document to
  disk. This surface must never gate the MCP/agent path — it is a human convenience layered on top
  of a mechanism that already works headlessly.
- **MCP**: a dedicated tool (name TBD, e.g. `aibt_migrate_document`) performs the same explicit
  persist, mirroring `apply_domain_patch`'s own dry-run-then-explicit-apply shape (`P6-006`).

**Diff preview** reuses the existing canonical writer (`Authoring/Serialization/Json/
CanonicalTreeJsonWriter.cs`) to render both the original and the in-memory-migrated document as
canonical JSON, diffed as plain text — no new diffing infrastructure, satisfying `data-formats.md`'s
"previewable diff" requirement directly with an existing, already-deterministic serializer.

## Spike evidence

`Spikes~/MigrationToolingDecision/` (disposable, run live via Unity MCP against the real open
Editor, never committed to `Tests/` — mirroring `P5-001`/`P3-001`'s own precedent) proves the
mechanism against a real fixture, not a synthetic same-version no-op:

- A real fixture node type, deliberately bumped from a real registered v1 shape to a v2 fixture with
  one added-with-default field and one renamed field.
- A real authored `.aibt.json` document referencing the v1 shape.
- The spike's own rule registry + engine migrates the document in memory; the result compiles
  successfully against the v2 manifest through the real, accepted `ReferenceCompiler` — not a mock.
- A diagnostic-shaped result names the tree/node/version-pair/field changes, matching the shape
  proposed above.
- A real text diff between original and migrated canonical JSON is produced and inspected.
- A negative case (v1 → v2 with a field *removed*, no rule registered) hard-fails through the
  existing `UnsupportedNodeVersion` path, unchanged — proving the unhandled-category side is
  disclosed, not silently forced through.

See `Planning~/Evidence/P7-005/README.md` for the full spike output.

## Consequences

- `P7-006` implements this ADR as real production code: the rule-registry type and engine
  (`Authoring/Migration/`, new), the two new `validate`/`compile` code paths that apply it
  in-memory, the proposed diagnostic (exact code confirmed against `TreeValidationDiagnosticCatalog`'s
  actual next-free slot at implementation time), the Editor notification surface, and the MCP
  `aibt_migrate_document` tool (or equivalent).
- No production code ships from this card. The spike is disposable, per this card's own Forbidden
  changes.
- Field removal and type-change remain genuinely unhandled after `P7-006` ships — any future card
  wanting to cover them needs its own decision cycle, since neither is "mechanically safe" the way
  add-with-default/rename are (a removed field may have carried meaning nothing else can infer; a
  type change needs a real conversion function, which reopens exactly the "custom code at the
  authoring layer" design space this ADR deliberately kept narrow).
- The exact new diagnostic code number is not reserved by this ADR — `P7-006` confirms
  `TreeValidationDiagnosticCatalog`'s actual next-free code at implementation time and updates this
  ADR's reference if it differs from `AIBT2042`.
