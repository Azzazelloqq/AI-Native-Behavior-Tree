# P11-001 — Subtrees: reuse one tree inside another (feature F1)

Status: `In Progress` — ADR accepted 2026-11-19 (AIBT-049) with all eight recommendations of its §8;
stage 1 approved the same day and implemented the same day (local commits on
`feature/aibt-consumer-workflow`; evidence in `Planning~/Evidence/P11-001/README.md`, accepted after the
full run of all nine assemblies on `7e614b8`). Stage 2 approved 2026-11-19 together with the owner's
decision that generated `bindings` values may name expanded keys `r:key` (ADR "Owner decisions",
item 9). Stage 2 implemented 2026-11-19 (local commits on `feature/aibt-consumer-workflow`; evidence in
the "Stage 2" section of `Planning~/Evidence/P11-001/README.md`), accepted after the full run of all
nine assemblies on `c02b42b`. Stage 3
(graph editor) approved 2026-11-19 together with two stage-2 follow-ups (import status `Needs registry`;
tree-ID index also covers `Packages/`). Stage 3 and both follow-ups implemented 2026-11-19 (local
commits on `feature/aibt-consumer-workflow`; evidence, screenshots and Play Mode clips in the "Stage 3"
section of `Planning~/Evidence/P11-001/README.md`), accepted after the full run of all nine assemblies
on `7794cb0`. Stage 4 needs the owner's
go-ahead.

## Objective

A tree author can use one tree as a node in other trees (and several times in one tree), edit it
once, connect its blackboard keys or pass constants, and see inside it while debugging — in the
graph editor, in `.aibt.json`, through MCP/CLI, on the managed agent, on native hosts and in the
simulation container. Existing trees, compiled programs, ABI v2 and the trace format do not change.

## Decision record

`Documentation~/decisions/ADR-P11-001-subtrees.md` (Accepted 2026-11-19, AIBT-049; the owner's
answers are in its "Owner decisions" section). Decided: authoring-time
expansion — a reference node is replaced by a renamed copy of the subtree before compilation
(inner node `n` at reference `r` becomes `r:n`); keys connect through a declared subtree interface;
tree format version 3 (additive). Rejected: runtime call node (changes compiled and trace formats),
copy-on-insert templates (no "edit once").

## Depends on

- Owner's answers to ADR §8 (given 2026-11-19, all as recommended).
- P10-014 (accepted 2026-11-19, `c68df77`; this feature does not touch `Runtime/Execution/**`).

## Required reading

- `Documentation~/specifications/canonical-json-v1.md`, `identity-and-hashing-v1.md`,
  `compiled-program-v1.md`, `blackboard-v1.md`, `agent-shared-blackboard-v1.md` (bindings, scope
  contracts), `diagnostics-v1.md`, `editor-layout-v1.md`.
- `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md`,
  `ADR-P7-042-tree-source-assets.md`, `ADR-P10-013-non-restarting-reactive-composites.md` (house
  style), this task's ADR.
- `Planning~/Evidence/P6-006/README.md` (extract/inline are payload moves).

## Scope (after approval, by stage; see ADR §5 and §7)

- Stage 1: `Schemas~/tree.schema.json`; `Authoring/Model/Tree/`; `Authoring/Serialization/Json/`;
  `Authoring/Validation/TreeValidator.cs`, `TreeValidationDiagnostics.cs`; new
  `Authoring/Compilation/Subtrees/`; additive overloads in `ReferenceCompiler.cs`,
  `Agents/Runtime/ManagedTreeProgram.cs`, `Agents/BehaviorTreeSource.cs`; behaviour-case runner;
  spec edits of ADR §4.8; tests and fixtures.
- Stage 2: `GeneratedCompiledProgramV2.cs` overload; `Editor/Graph/BehaviorTreeImporter.cs`,
  `TreeCatalogEntry.cs`, `ProductionTreeAssetBaker.cs`, `ProductionTreeAssetStaleness.cs`;
  `Runtime/Integration/ProductionTreeAsset.cs` (additive serialized field).
- Stage 3: `Editor/Graph/**` (palette, reference node view, inspector, breadcrumb, extract/unpack,
  live debug, `MatchesProgram`), `Editor/Trace/TraceTimelineWindow.cs`.
- Stage 4: `MCP/**`, `MCP~/Server/**`, `MCP~/Cli/Program.cs`, `Documentation~/cli.md`, user guide.

## Forbidden changes

- Execution semantics of existing nodes; both executors (`Runtime/Execution/**`).
- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`; compiled program format and version;
  trace format; node registry / P7-028 authority; package version.
- Existing public API signatures (additive overloads only).
- Reading tree format versions 1 and 2 differently than today.
- A new compilation-policy field (it would change every program's policy hash).

## Deliverables

- Per ADR §7, stages 1–4, each with evidence in `Planning~/Evidence/P11-001/`; stages 3 and 4 with
  screenshots and a video.

## Acceptance criteria

- Every existing fixture and sample tree compiles to a byte-identical compiled-content hash.
- A tree with references compiles to the same program as the hand-written expansion.
- Diagnostics `AIBT2047-2050`, `AIBT3020-3028` each have a negative test.
- Reference and native executors agree on trees with subtrees (equivalence harness, no executor
  change); managed agent, `NativeTreeAgent` and a `TreeSimulation` group run them.
- Editing a subtree updates every dependent imported source and production asset.
- Hot reload by qualified node ID: editing a subtree migrates/restarts each instance correctly.
- Graph editor: insert, bind, open, extract, unpack, and live highlight of the right instance.
- MCP/CLI: add/rebind/extract/unpack/expand/usages tools with permission negatives.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter <new subtree test classes>   (one runner per Editor)
Tools/Unity/Invoke-UnityTests.ps1 -Assemblies AIBT.CodeGen.ContractTests  (ABI v2 unchanged)
dotnet harness for model/expander tests (see the Modules dotnet test harness)
Save-UnityWindow.ps1 / Save-UnityClip.ps1 for stages 3–4
```

## Handoff notes

- Diagnostic codes `AIBT2047-2050` and `AIBT3020-3028` were free on 2026-11-19 (checked across code,
  docs and planning); stage 1 now defines them.
- Stage 1 (2026-11-19): see the evidence README for the deviations to settle before stage 2 —
  `LatestFormatVersion` stays 2 (new `SubtreeFormatVersion = 3`); generated `bindings` values cannot
  contain `:`, so a binding to an expanded `r:` key reports `AIBT3026`; the expansion is always
  format 2; registered-type constants report `AIBT3026`; `BehaviorTreeImporter` is not wired yet
  (stage 2 uses the new 8-argument `BehaviorTreeSource.SetContent`).
- Stage 2 (2026-11-19): `BehaviorTreeImporter` is version 3 (one re-import of every `.aibt`). The
  importer expands against `ProjectNodeCatalog.Discover()`, which leaves out test assemblies, so a
  tree with subtrees whose nodes come only from a test or Editor assembly imports as `Errors`
  (`AIBT2021`) with its raw JSON, where a tree without subtrees shows `Needs registry`. The tree-ID
  index scans `Assets/` only (not `Packages/`). See the evidence README for the open questions.
- Stage 3 (2026-11-19): Editor code only, no public API added. Graph and MCP authoring edits of a
  format-3 tree now keep its `interface` (they dropped it before). Extract writes the new subtree
  file on the next Save (Undo leaves no file); Unpack keeps format 3. Demo trees for the evidence are
  in `Tests/Editor/Graph/Fixtures/SubtreeDemo/`. Open questions in the evidence README.
- ADR §9 lists three unrelated findings (duplicate `AIBT2042/2043` definitions, last-wins identity
  map, stale ADR-P7-042 importer sentence) for separate follow-up.
