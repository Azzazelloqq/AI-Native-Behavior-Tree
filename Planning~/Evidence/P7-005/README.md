# P7-005 migration-tooling design decision evidence

## Result

`Documentation~/decisions/ADR-P7-005-migration-tooling.md` (`AIBT-036`) proposes:

- **Scope**: only field-added-with-default and field-renamed. Field removal and type-change remain
  explicit hard failures, unchanged.
- **Mechanism**: a declarative, ordered `(NodeTypeId, sourceVersion)` migration-rule registry,
  operating purely on the authored document's JSON `parameters` block at the authoring-tooling
  layer — never inside the Burst-compiled node, so ABI v1's ban on node-execution migration
  callbacks does not apply.
- **In-memory only**: `validate`/`compile` apply the migration to an in-memory copy; the on-disk
  `.aibt.json` is never mutated as a side effect, matching `SemanticEditTransaction`'s own
  established "no silent mutation" pattern.
- **Structured, non-blocking diagnostics**: `Info`-severity, reachable through `explain_diagnostic`
  like every other AIBT diagnostic, so an MCP-driving AI agent sees exactly what changed without
  being blocked.
- **Persisting is separate and explicit**: an Editor notification (non-blocking, never gating the
  MCP path) and a dedicated MCP tool, both deferred to `P7-006`.
- **Diff preview** reuses the existing `CanonicalTreeJsonWriter` — no new diffing infrastructure.

This decision was reached through direct discussion with the owner in chat (not decided
unilaterally): the owner confirmed both automatic in-place recovery ("nothing should fall apart")
and structured surfacing of what changed ("tell the user/agent, worth a second look") are wanted
together, that persisting to disk must be a separate explicit action, and that any Editor UI must
never block the MCP/AI-agent path — this project is an AI-first library, and the primary consumer
of a diagnostic is often an agent, not only a human.

## Investigation (grounded facts, not assumed)

- `Authoring/Validation/TreeValidator.cs:487` — today's hard equality check
  (`node.TypeVersion != manifest.Version`), zero compatibility path, confirmed by direct read.
- Zero migration execution machinery exists anywhere for node types (repo-wide grep). The closest
  precedent, `MigrationSourceVersion`/`MigrationContractId` on `RegisteredUnmanagedTypeDescriptor`
  (`Runtime/Blackboard/Types/BlackboardTypeDescriptor.cs:151-202`), is declared metadata for
  blackboard *value types* with no consumer anywhere — confirmed by grepping every reference to
  those two fields (14 files, all either the declaration itself, a hash contribution, or generated
  public-API dumps — never an execution site).
- `burst-node-abi-v1.md:429-430` forbids custom migration callbacks inside a Burst node.
- No node type has ever been version-bumped in this project's history — confirmed: no
  `"version": 2` or higher in any committed fixture/sample/manifest.
- `Documentation~/data-formats.md:52-54` (normative) requires deterministic/testable/ordered/
  diffable migrations with structured-diagnostic failure for unsupported versions, never
  best-effort loading.

## Spike evidence

`Spikes~/MigrationToolingDecision/SpikeMigrationTooling.cs` (disposable, run live via Unity MCP
against the real open Editor as `AIBT.Editor.Tests`, archived after this session — never shipped as
production test surface):

- `RealVersionBump_MigratesInMemoryAndCompilesAgainstV2Manifest`: a real fixture node type
  (`aibt.core.spike-migrated-node`) registered at v2 with a renamed field (`moveSpeed` → `speed`)
  and an added field (`acceleration`, default `5`). A real authored `.aibt.json`-shaped
  `TreeDocument` at v1 is migrated in memory by a real rule-application function (not a mock),
  producing a diagnostic-shaped result naming both changes. The migrated document then compiles
  successfully against the v2 manifest through the real, accepted `ReferenceCompiler` — confirmed
  by asserting `result.Success == true`. **Passed live.**
- The same test also proves the diff-preview approach: `CanonicalTreeJsonWriter.Write` (the real,
  existing canonical writer) renders both documents; the real recorded diff shows exactly
  `"moveSpeed": 10` → `"speed": 10` (with `"acceleration": 5` newly present), logged as real
  evidence (`AIBT_P7_005_SPIKE_DIFF`, captured below), not hand-written.
- `UnregisteredVersionGap_StillHardFailsThroughTheExistingValidator`: a v2-authored document
  against a registry that only knows v3 (a genuine field-removal case, no migration rule
  registered anywhere) fails compilation with `TreeValidationDiagnosticCodes.UnsupportedNodeVersion`
  — the exact same diagnostic this codebase already produces today, unchanged. **Passed live.**

Both tests passed on the first fix-up run after two real construction issues were found and
corrected (not assumed correct): `NodeParameterContract`'s third constructor argument is `required`
(a `bool`), not a default value, confirmed by reading the real constructor; and a
`NodeManifestSource`-appropriate type-ID namespace is enforced by `NodeRegistryBuilder.ValidateSource`
(`aibt.core.` for `BuiltIn`, `aibt.test.` for `TestFixture`, anything else forbidden for
`UserExtension`) — the spike's fixture type initially used a `UserExtension`-shaped ID with a
`BuiltIn`-style registration call, which both `AIBT3004` (wrong namespace) and, after that fix,
`AIBT3012` (`UserExtension` alone has no Phase 1 reference-handler binding) caught live. Fixed by
registering the fixture as `aibt.core.spike-migrated-node` via `AddBuiltInForTest` with an explicit
`NodeHandlerBindingContract`, mirroring `ReferenceCompilerTests.RegistryWithFixtures`'s own
established pattern exactly.

Full regression: `AIBT.Editor.Tests` (376/376) passed unchanged after the spike was archived out —
no production file was touched by this card, per its own Forbidden changes.

### Real recorded diff (from the live test run)

```json
before={
  "format": "aibt.tree",
  "formatVersion": 1,
  "treeId": "tree.spike.migration",
  "root": "root",
  "nodes": {
    "root": {
      "type": "aibt.core.spike-migrated-node",
      "typeVersion": 1,
      "parameters": {
        "moveSpeed": 10
      }
    }
  }
}

after={
  "format": "aibt.tree",
  "formatVersion": 1,
  "treeId": "tree.spike.migration",
  "root": "root",
  "nodes": {
    "root": {
      "type": "aibt.core.spike-migrated-node",
      "typeVersion": 2,
      "parameters": {
        "acceleration": 5,
        "speed": 10
      }
    }
  }
}
```

## Decision

`ADR-P7-005` (`AIBT-036`), **Status: Accepted 2026-09-02**. Per `Planning~/DECISION_BOUNDARIES.md`,
this was a "must escalate" decision (persisted-format-adjacent, new diagnostic code range) — the
owner reviewed the completed ADR and spike evidence and accepted it as-is.

## Scope and limitations

- No production code ships from this card — `Spikes~/MigrationToolingDecision/` is disposable, per
  this card's own Forbidden changes. `P7-006` implements the ADR as real production code.
- Field removal and type-change remain genuinely unhandled after `P7-006` ships (disclosed in the
  ADR's own Consequences section), not silently forced through.
- The exact new diagnostic code number (`AIBT2042`, proposed) is not reserved by this card —
  `P7-006` confirms `TreeValidationDiagnosticCatalog`'s actual next-free code at implementation
  time.

See `verification-results.json` for exact commands and results.
