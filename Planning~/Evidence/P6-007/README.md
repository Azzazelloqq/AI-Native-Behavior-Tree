# P6-007 MCP verification tools evidence

## Result

Done. `MCP/Verification/` implements all 4 tools `ai-and-mcp.md`'s Verification section lists
that this card owns -- `validate`, `compile`, `simulate`, `explain-diagnostic` -- each wrapping
exactly one already-accepted production entry point (`TreeValidator`, `ReferenceCompiler`,
`ReferencePreviewDriver`). No second validator/compiler/executor exists anywhere in this card's
code. Wired through `McpToolDispatcher.cs` (4 new permission-tagged cases) and relayed by 4 new
thin server methods in `MCP~/Server/VerificationTools.cs`.

## Real gaps found (before/while implementing)

1. **Only 2 of ~12 `DiagnosticCatalog` instances in the whole codebase are reachable from a new
   `AIBT.Mcp` module.** `AIBT.Authoring`'s and `AIBT.Runtime`'s `InternalsVisibleTo` grants
   (`Authoring/AssemblyInfo.cs`, `Runtime/AssemblyInfo.cs`) list only test assemblies, never
   `AIBT.Mcp`. Of every `new DiagnosticCatalog(...)` site (confirmed by direct inspection: tree
   validation, blackboard schema, tree/layout JSON, the compiler, the node registry, runtime
   execution, async commands, blackboard storage), only `TreeValidationDiagnosticCatalog.Catalog`
   (`AIBT2010`-`2041`) and `BlackboardDiagnosticCatalog.Catalog` (`AIBT2001`-`2008`) are public
   classes with a public `Catalog`. Everything else is `internal`/`private`. **Disclosed, not
   escalated** (this is a pre-existing fact about the codebase's own accessibility, not a new
   architectural choice; widening `InternalsVisibleTo` would be a cross-assembly change outside
   this card's Allowed changes): `explain-diagnostic` looks up a code in these two catalogs only;
   any other code reports `catalogReachable: false` honestly rather than fabricating a
   description.
2. **`ReferencePreviewDriver` (the only simulation entry point this card may call) has no
   event/completion/resume/abort injection API, no step-budget parameter, and no caller control
   over `updateId`/`snapshotRevision`/`treeInstanceId`/`rootSeed`** -- confirmed by reading its
   full public surface (`TryCreate`, `BeginTick`, `StepAtomic`, `RunTick`, `Restart`,
   `CaptureInspection` only; `BeginTick`/`RunTick` take only `timeMicroseconds`, and the driver
   assigns `updateId`/`Revision` itself sequentially starting at 1, hardcoding `TreeInstanceId(1)`
   with no seed parameter anywhere). `simulate` therefore accepts only `behavior-case-v1.md`'s
   plain `update` steps, rejects any step carrying `events`/`completions`/`stepBudget` or any
   non-`update` operation with a structured diagnostic, and validates the caller's supplied
   `updateId`/`snapshotRevision` against the driver's own sequential assignment (rejecting a
   mismatch) rather than silently discarding fields it cannot honor. `treeInstanceId`/`rootSeed`
   are not accepted as input at all. This is the same scope boundary the card's own
   Forbidden-changes clause already anticipates.
3. **No real `*.aibtcase.json` fixture is actually drivable through `ReferencePreviewDriver`**,
   found while selecting a live/test target: `positive-minimal.aibtcase.json`'s `"tree"` field
   names `Trees/minimal.aibt.json`, which does not exist anywhere in the repository -- the test
   that uses this case (`BehaviorCaseRunnerTests.cs`) drives a mock executor, never a real tree.
   `patrol-react.aibtcase.json`'s real tree (`Tests/Fixtures/Golden/Trees/patrol-react.aibt.json`)
   uses `aibt.test.alert-condition`/`aibt.test.raise-alert` -- node types outside
   `ReferencePreviewFixtureEnvironment`'s Phase 1 fixture set entirely. **Decision**: `simulate`'s
   tests and live verification instead target `Tests/Editor/Preview/Fixtures/success-then-running
   .aibt.json` (`P3-009`'s own proven fixture, already known to compile against exactly
   `ReferencePreviewDriver`'s registry, per `ReferencePreviewParityTests.cs`) with a
   hand-authored minimal `update`-step-only case wrapping it -- a real, disclosed substitution,
   not a silent one.
4. **`TreeValidator.Validate` takes a `TreeValidationPolicy`/`ValidationOptions`, never the
   `ProjectPolicySnapshot` type `.aibt/policy.json` is already read into** (`P6-003`/`P6-005`,
   `Authoring/Discovery/ProjectPolicySnapshot.cs`) -- the two types are independent, with no
   existing conversion anywhere (`ProjectPolicySnapshot.cs`'s own doc comment: "this is not
   TreeValidator's TreeValidationPolicy... Discovery reports policy facts, it does not run
   validation"). `McpVerificationJson.ToValidationPolicy` maps them field-by-field, confirmed
   exact 1:1 string-enum correspondence against `Schemas~/policy.schema.json`'s own declared
   enums (`blackboardNaming`: `snake_case`/`camelCase`/`PascalCase`/`any`; `unreachableNodes`:
   `error`/`warning`/`allow`). When `.aibt/policy.json` is absent or unreadable, `validate` falls
   back to a policy-free `ValidationOptions` and reports `policyApplied: false` honestly, mirroring
   how `get_project_manifest` already discloses the same read failure rather than substituting a
   different file.
5. **The existing diagnostic JSON in `MCP/Authoring/McpAuthoringToolDispatcher.cs` (`P6-006`) is a
   hand-rolled, non-canonical writer** that silently drops `treeInstanceId`, `documentId`,
   `line`/`column`, `relatedLocations`, and `suggestedOperation`. The real canonical entry point
   `diagnostics-v1.md` itself names is `AIBT.Authoring.DiagnosticJson.Serialize(AuthoringDiagnostic)`
   (backed by the internal `CanonicalDiagnosticJsonWriter`, exact property order per
   `diagnostics-v1.md`'s own "Authoring JSON" section). This card's tools use the real canonical
   serializer, looping per-diagnostic (it serializes one at a time) and reassembling a JSON array
   -- confirmed byte-for-byte identical to a direct `TreeValidator.Validate` call's own output by
   a dedicated EditMode test. `P6-006`'s own files were not touched (outside this card's Allowed
   changes); the inconsistency between the two cards' diagnostic JSON shapes is disclosed here as
   known follow-up work, not silently fixed retroactively.

## Implementation

`MCP/Verification/`:
- `McpVerificationDiagnostics.cs` -- `AIBT9022`-`9024` (tree not found, malformed arguments,
  unsupported simulate step).
- `McpVerificationJson.cs` -- `WriteDiagnostics` (the real canonical serializer, reused not
  reimplemented), `ToValidationPolicy`/`ToUnreachableNodePolicy` (the policy mapping), and
  `ReadUpdateStep` (the restricted behavior-case step reader).
- `McpVerificationToolDispatcher.cs` -- the 4 handlers. `Validate`/`Compile` resolve the tree via
  `AIBT.Mcp.AibtTreeDiscovery` (already public, same assembly, reused from `P6-005`/`P6-006` --
  not duplicated) and compile/validate against `NodeRegistryBuilder.CreateWithBuiltIns().Build()`
  (the real project registry, distinct from `simulate`'s fixed Phase 1 fixture registry).
  `Simulate` labels every response with the exact backend/node-set text the card's own
  Forbidden-changes clause requires. `ExplainDiagnostic` echoes a caller-supplied
  `suggestedOperation` verbatim (via `DeepClone`, never re-derived) or omits the field entirely --
  never fabricates one.

`MCP~/Server/VerificationTools.cs`: 4 thin relays mirroring `AuthoringTools.cs`'s exact shape
(JSON-text parameters for `simulate`'s steps array and `explain_diagnostic`'s diagnostic object).

`McpToolDispatcher.cs`: 4 new cases -- `validate`/`compile` -> `McpPermissionCategory.Compilation`
(both are compiler-adjacent: `ReferenceCompiler.Compile` runs `TreeValidator.Validate` internally
too), `simulate` -> `TestExecution`, `explain_diagnostic` -> `Read` (a pure, project-independent
lookup).

## Unity EditMode tests (14, all real, run live against `6000.5.8f1`)

`Tests/Editor/Mcp/Verification/McpVerificationToolDispatcherTests.cs`, calling the real
`McpToolDispatcher.Dispatch` entry point:
- `validate` on a deliberately invalid tree (`aibt.core.inverter` with no child, the exact
  `AIBT2023` child-count-policy violation) compared **byte-for-byte** against a direct
  `TreeValidator.Validate` call's own `DiagnosticJson`-serialized output; `validate` on a valid
  tree reports `valid: true` with zero diagnostics.
- `compile` compared against a direct `ReferenceCompiler.Compile` call's own compiled content
  hash; `compile` on the invalid tree returns `success: false` plus real diagnostics, never a
  bare boolean.
- `simulate` against `success-then-running.aibt.json`, asserting the exact trace
  (`NodeEntered`/`NodeTicked`/`NodeExited` for the `Success` leaf, `NodeEntered`/`NodeTicked` for
  the always-`Running` leaf) and `Progress.Waiting`/`rootResult: null` -- matching `P3-009`'s own
  already-proven oracle behavior for this exact fixture (`ReferencePreviewParityTests.cs`); a
  step carrying `events` rejected with `AIBT9024`; a step with a mismatched `updateId` rejected
  with `AIBT9024`.
- `explain_diagnostic` for `AIBT2023` (reachable, `SemanticValidation` subsystem) with and
  without a supplied `suggestedOperation` (echoed verbatim, or omitted -- never invented); for
  `AIBT3010` (compiler-owned, unreachable) reporting `catalogReachable: false`.
- The full 4-tool permission-negative matrix (each tool rejected when granted a different
  category than its own).

## Live end-to-end verification (real MCP client, real permanent server, real Unity bridge)

Bridge started live in the actually-open Unity `6000.5.8f1` Editor via Unity MCP `execute_code`
(mirroring `P6-005`/`P6-006`'s own methodology). The official `@modelcontextprotocol/inspector`
CLI, configured via `.mcp.json`-shaped `--config`/`--server` files with `--tool-args-json` (both
documented workarounds from `P6-005`/`P6-006`'s own evidence), against the real, permanent
`MCP~/Server/` (`dotnet run --project Assets/AIBT/MCP~/Server`): `tools/list` showed all 4 new
tools plus every prior tool with real schemas. A real fixture tree
(`tree.mcp-verify-live-test`, written directly to the actual `Modules` project's `Assets/`) was
validated and compiled: an invalid version (`aibt.core.inverter` with no child) returned the same
real `AIBT2023` diagnostic, full canonical shape (`documentId`/`jsonPointer` included, exact
`diagnostics-v1.md` property order); a valid version compiled successfully with a real content
hash. `aibt_simulate` was called against the real, already-existing project fixture
`tree.test.preview-success-then-running` (no live file needed) and reproduced the exact trace
sequence the EditMode test independently proves. `aibt_explain_diagnostic` returned real catalog
facts for `AIBT2023`. A permission-negative call (`aibt_validate` with only `Read` granted)
correctly returned `AIBT9012`. The live fixture tree was deleted afterward; the Editor console
was clean on the following refresh.

## Verification

```text
Unity MCP run_tests (EditMode): AIBT.Tests.Editor.Mcp.Verification.* -- 14/14 passed
Unity MCP run_tests (EditMode): Mcp.Discovery + Mcp.Authoring + Patching + Editing regression --
  62/62 passed, no regressions
dotnet build MCP~/Server -- 0 warnings, 0 errors
Live: real bridge + real permanent MCP~/Server/ + official Inspector CLI --
  tools/list (4 verification + 11 authoring + 3 discovery, real schemas)
  validate/compile on a real invalid tree -> real AIBT2023, full canonical shape
  compile on a real valid tree -> real content hash
  simulate against the real project fixture tree.test.preview-success-then-running -> exact
    proven trace sequence
  explain_diagnostic for a reachable code -> real catalog facts
  permission-negative: validate with only Read granted -> AIBT9012
  live fixture cleaned up; Editor console clean on the following refresh
Tools~/Verification/Verify-Static.ps1 -- passed, 95 work items
git diff --check -- clean
```

## Addendum (2026-08-28): diagnostic JSON writer shared with P6-006, no longer duplicated

Finding 5 above flagged P6-006's hand-rolled diagnostic JSON as known follow-up work. Fixed in the
same session that fixed a real P6-006 data-loss bug (see `Planning~/Evidence/P6-006/README.md`'s
2026-08-28 addendum): this card's own `WriteDiagnostics` (previously local to
`McpVerificationJson.cs`) moved to a new neutral `MCP/McpDiagnosticJson.cs`, now the single
diagnostic-collection writer both `MCP/Authoring/` and `MCP/Verification/` call. No behavior change
to this card's own tools or tests -- `DiagnosticJson.Serialize`'s output is unchanged, only which
class invokes it.

## Addendum (2026-08-28): explain-diagnostic reaches 5 catalogs now, owner-approved widening

Same fix session as the other 2026-08-28 addenda above (item 4). Finding 1's own "Widening this
would require an `InternalsVisibleTo` grant... flagged here for a future card or owner decision"
-- the owner explicitly approved that widening. `Authoring/AssemblyInfo.cs`, `Runtime/AssemblyInfo.cs`
(both edited) and a new `Editor/AssemblyInfo.cs` (`AIBT.Editor` had none before) now all grant
`InternalsVisibleTo("AIBT.Mcp")`.

Re-checking every `DiagnosticCatalog` holder's *actual* accessibility (not just its containing
class) before updating `explain-diagnostic`, rather than assuming the grant alone was sufficient,
found the real picture is more nuanced than "internal vs. public": 3 more catalogs became
genuinely reachable --
`TreeJsonDiagnostics.Catalog` (`AIBT1001`-`1008`), `NodeRegistryDiagnostics.Catalog`
(`AIBT3001`-`3005`), `LayoutJsonDiagnostics.Catalog` (`AIBT1101`-`1111`) -- but 4 others stay
unreachable regardless of any `InternalsVisibleTo` grant, because each declares its own `Catalog`
field `private`, not `internal`: `ReferenceCompilerDiagnostics` (`AIBT2042`-`2046`,
`AIBT3010`-`3019`), `ReferenceExecutionDiagnostics` (`AIBT4001`-`4008`), `CommandAsyncDiagnostics`
(`AIBT4101`-`4110`), `BlackboardStorageDiagnostics` (`AIBT4201`-`4209`). Per explicit owner
decision, these 4 are left as a disclosed, found-but-not-fixed limitation rather than also
widening their field accessibility (a different, smaller change touching already-accepted
Runtime/Authoring files outside this pass's scope).

This also corrected a stale assumption in the original 6-item fix-session brief, which expected
`AIBT3010` to become reachable after the grant -- it does not (its catalog is private). The
existing "reports unreachable" test previously used `AIBT3010` for that reason; it now uses
`AIBT9012` (`McpDiagnostics.PermissionDenied`) instead, since MCP's own `AIBT9xxx` tool-level codes
have no `DiagnosticCatalog` anywhere and are therefore permanently unreachable, a more robust proof
than a code whose reachability depends on another file's field-access modifier.

Verified: `AIBT.Tests.Editor.Mcp.*` -- 67/67 passed (64 pre-existing + 3 new parametrized cases,
one per newly-reachable catalog); a full project-wide EditMode run -- 1531/1534 passed, the 3
failures being pre-existing host-project noise unrelated to AIBT (`AddressableAssets`,
`LocalSaveSystem`, and a `CodeGen` assembly-identity test — the same category of host-project noise
`P3-013`/`P4-009`/`P5-010`'s own gate evidence already documented as not reproducing in a clean
detached harness). `Verify-Static.ps1` passed (95 work items). `git diff --check` clean.

## Scope and limitations

- `explain-diagnostic` can look up codes in 5 catalogs now (fixed 2026-08-28, see the Addendum
  above): `TreeValidationDiagnosticCatalog` (`AIBT2010`-`2041`), `BlackboardDiagnosticCatalog`
  (`AIBT2001`-`2008`), `TreeJsonDiagnostics` (`AIBT1001`-`1008`), `NodeRegistryDiagnostics`
  (`AIBT3001`-`3005`), and `LayoutJsonDiagnostics` (`AIBT1101`-`1111`). Four more subsystems'
  diagnostics (compiler `AIBT3010`-`3019`/`2042`-`2046`, runtime execution `AIBT4xxx`, async
  commands, blackboard storage) report `catalogReachable: false` -- not from a missing
  `InternalsVisibleTo` grant anymore, but because each holder's own `Catalog` field is `private`, a
  disclosed limitation left to a future card or owner decision rather than fixed silently.
- `simulate` supports only plain `update` steps with driver-assigned sequential
  `updateId`/`snapshotRevision`; no events, completions, resume, abort, step budget, custom tree
  instance ID, or root seed -- inherited directly from `ReferencePreviewDriver`'s own public
  surface (the same limitation `P3-009`'s editor preview already has), not a new restriction this
  card invented.
- `validate`'s project-policy support only reads `.aibt/policy.json` at the project root (sibling
  to `Assets/`), same resolution `get_project_manifest` already uses; `compile` does not apply
  project policy at all (only `ReferenceCompilationPolicy.Phase1`), matching the card's own
  deliverable text (project-policy diagnostics are `validate`'s requirement, not `compile`'s).
- Single client, single Unity instance at a time, same as every prior P6 card's own disclosed
  scope, unchanged here.
