# P7-009 — Generic native-dispatch translator production implementation

Status: `Done`

## Objective

Apply `ADR-P6-022` (Accepted, spike completed) to production: build
`GenericNativeDispatchTranslatorV1`'s proven design as real production code, and widen `P6-009`'s
`test-node` MCP tool to actually drive generated dispatch for built-in-typed, non-async,
single-case-reachable node shapes instead of stopping at compile-clean + registry-materialization-valid.

Bundled into this card (mechanical, not requiring its own decision cycle, mirroring `P6-021`'s own
precedent for small disclosed defects): fix the real, disclosed `generate_node` condition-template
compile failure for a `Bool`-typed blackboard read (`current >= config.Minimum` does not compile
when `config.Minimum` is `bool`), found live during the `P6-012` gate session.

## Depends on

- `P6-022` (the accepted decision and completed spike this card implements).
- `P6-009` (the `test-node` tool this card widens).

## Required reading

- `Documentation~/decisions/ADR-P6-022-generic-native-dispatch-test-harness.md`, including its
  2026-08-31 spike addendum — **read the addendum's dispatch-index-contiguity finding carefully**:
  a specific node inside an existing, already-larger catalog is reachable only by translating that
  catalog's full `0..targetIndex` case prefix, not by isolating one case. `test-node` almost always
  targets a node inside a project's own growing catalog (not index 0), so this card's own
  implementation must translate the prefix, not just the single target case the spike proved.
- `Spikes~/GenericNativeDispatchTestHarness/` (the proven translator and its own real, empirically-found
  requirements: canonical-range array sizing, name-based encoding/kind mapping).
- `MCP/NodeDevelopment/NodeTemplateGenerator.cs` (the disclosed `Bool`-typed condition-template
  defect this card also fixes).

## Allowed changes

- A new production translator (expected `Runtime/Execution/Burst/Dispatch/` or
  `Authoring/Registry/Generated/`, per the ADR's own Forbidden-changes note about where a future
  implementation card may touch — confirm the exact location against `architecture.md`'s dependency
  direction before choosing).
- `MCP/NodeDevelopment/` (widening `test-node`; fixing the template defect).
- `Tests/Editor/Mcp/NodeDevelopment/`, `Tests/Runtime/NativeExecution/Dispatch/` (new).
- `Planning~/Evidence/P7-009/`.

## Forbidden changes

- Claiming coverage for `Registered`-encoded fields or the `AsyncOperation`/`Completion` binding
  pair — both remain explicitly unproven per the ADR; this card does not extend scope beyond what
  was proven.
- Silently falling back to per-node hardcoded offsets anywhere in the new production translator —
  the entire point is driving dispatch purely from compiled metadata.

## Deliverables

- The production translator, built from `CanonicalDescriptorJson` (via
  `GeneratedShardMetadataMaterializer`) and the generated catalog's own reflected fingerprints,
  extended from the spike's single-case proof to translate a real catalog's `0..targetIndex` case
  prefix for whichever node `test-node` targets.
- `test-node` widened to actually drive `ExecuteImmediate` for a node within the proven scope
  (built-in-typed fields, non-async/non-Completion bindings), reporting a structured, honest
  "out of proven scope" result for `Registered`/async/Completion node shapes rather than a false
  pass or an opaque failure.
- The `generate_node` `Bool`-typed condition-template fix, with a regression test proving a
  generated `Bool`-parameter condition node now compiles.

## Acceptance criteria

- `test-node` run against a real, freshly generated, non-index-0 custom node (via the same
  generate-preview-compile-apply gate `P6-009`'s own evidence already exercises) actually drives
  generated dispatch and reports a real tick result, not just compile-clean/registry-valid.
- `test-node` run against a node using an out-of-scope shape (a `Registered` value type, or an
  `AsyncOperation` binding) reports the gap honestly (a structured "not yet supported" result), never
  a false pass.
- The `Bool`-typed condition-template fix is proven by a real generate-compile round trip through
  the real Roslyn analyzer, not a hand-written fixture standing in for the generator's own output.
- Regression: `P6-009`'s own existing 18 tests plus the full project suite pass unchanged.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
test-node live proof: a real, freshly generated non-index-0 custom node driven through real dispatch
test-node negative proof: an out-of-scope node shape reports honestly, not falsely
generate_node Bool-condition-template fix proven via a real generate-compile round trip
live interactive proof via Unity MCP against the real open Editor
```

## Handoff notes

- If a future project needs `Registered`-encoded fields or async/Completion node shapes driven
  through `test-node`, that is new research, not an extension of this card's own proven scope.

## Outcome

Done, with one owner-approved re-scoping. Investigation before implementation found the
"non-index-0 custom node via test-node" acceptance criterion structurally unreachable through
today's staging architecture (`StagingSlot` always stages exactly one node, always dispatch index 0)
-- surfaced directly to the owner, who approved building the translator's full `0..targetIndex`
prefix support anyway and proving it against a dedicated permanent fixture instead of through the
live tool. `GenericNativeDispatchTranslatorV1` (`Authoring/Registry/Generated/`, not
`Runtime/Execution/Burst/Dispatch/` -- a real dependency-direction correction) and
`GenericNodeDispatchRunner` (`MCP/NodeDevelopment/`) drive real generated dispatch for `test-node`;
a real `[AibtCatalogSet]`-in-a-separate-assembly requirement was discovered empirically
(`AIBT5011`) and fixed via a companion `Pending/Catalog/` staging sub-assembly. The `Bool`-typed
condition-template bug is fixed. All three -- real dispatch, honest out-of-scope reporting, and the
Bool fix -- proven live against the real open Editor; the prefix-translation path proven by a new
permanent 3-node fixture (`Tests/Editor/CodeGen/Dispatch/`). Full detail in
`Planning~/Evidence/P7-009/README.md`.
