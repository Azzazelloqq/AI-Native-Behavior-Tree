# P11-010 — A native node that reads and writes one key through one ReadWrite binding can stop the host

Status: `Review` — the fix is committed on `feature/aibt-consumer-workflow` and accepted by the
orchestrator after the full run of ten assemblies on `0c2b541` (all green).

## Objective

P11-009 found, and worked around by splitting its fixture node, that a native node which reads and then
writes one key through a single `ReadWrite` binding opens two value sessions per tick; in a catalog whose
largest case has no spare bindings the per-frame session capacity
(`GeneratedBurstCatalogV2.CreateWorkspaceCapacity`) is exceeded and the host stops with
`NativeLifetimeStateInvalid` ("Callback rejected: CapacityExceeded").

1. Reproduce with the smallest such node in a catalog where it is the largest case, on `ProductionTreeHost`
   and `NativeTreeAgent`; state the exact condition (how the capacity is computed, how many sessions a
   ReadWrite binding opens, which catalogs have enough room by accident, whether a library or sample node
   has the shape). Save the probe and its output.
2. If the cause is in the runtime or editor-side code: a test that fails before, the fix, proof that the
   bytes and content hash of programs that compile today do not change, that `NativeLibraryAliasedKeyTests`
   and `MixedScopeNativeTreeTests` stay green in every scheduling policy they cover and that the update path
   allocates nothing new; then decide whether the P11-009 fixture goes back to one node.
3. Stop instead if the cause is in the generator or the code it emits, or the fix needs a new ABI member, a
   format change or a larger capacity for every catalog with a measurable memory cost.

## Depends on

- `P11-009` (batch 3; the follow-up is its F9 "Not verified" item 1).

## Scope

- `Runtime/Integration/GeneratedDispatch/GeneratedBurstCatalogV2.cs` (`CreateWorkspaceCapacity`).
- A fixture node assembly `Tests/Editor/CodeGen/GenerationP11010/`;
  `Tests/Integration/NativeRuntime/ReadWriteBindingSessionTests.cs`; the reference in
  `Tests/Integration/AIBT.Integration.Tests.asmdef`; the comment of
  `Tests/Editor/CodeGen/GenerationP11009/MixedScopeFixtureNodes.cs`.
- `CHANGELOG.md`, `Planning~/work-items.json`, `Planning~/MASTER_PLAN.md`, `Planning~/Evidence/P11-010/`.

## Forbidden changes

- Execution semantics, `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format,
  the bytes or content hash of any program that compiles today, the generator in `CodeGen~` and the code it
  emits, `package.json`, existing low-level APIs (only additions), normative specifications, files under
  `Tests/Fixtures`.

## Acceptance criteria

- The condition stated exactly and shown by a probe whose source and output are saved.
- A fix with its failing-before test, or a stop with options, costs and a workaround.
- Programs that compile today keep their bytes and content hash; the aliased-key and mixed-scope tests green
  in every policy they cover; no new allocation on the update path.
- The tests run listed with counts; the Console free of new errors; the submodule checkout clean.
- `Planning~/Evidence/P11-010/README.md` with commits, the condition, before/after with raw file names, the
  tests run and what was not verified.
- `Tools~/Verification/Verify-Static.ps1` passes.
