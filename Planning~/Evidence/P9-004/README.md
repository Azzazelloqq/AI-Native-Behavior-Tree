# P9-004 — Native agent facade and automatic preparation

Status: Review, verified in the `Modules` host project on Unity 6000.7.0b1, 2026-11-15. Local
commit only, no push. Decision: [ADR P9-004](../../../Documentation~/decisions/ADR-P9-004-native-tree-agent-facade.md) (AIBT-046).

## Result

1. **Facade.** `Runtime/Integration/NativeTreeAgent.cs` (`AIBT.Runtime`, no new dependency) and
   `NativeTreeProgramCache.cs` (internal, ref-counted program per asset and catalog factory, reset
   on `SubsystemRegistration`). `SetFact<T>` for Bool, Int32, Int64, Float32, Float64, Float2,
   Float3, Quaternion and `Vector3`; handles resolved once per host and flushed in
   `TryFlushInputs`; last values re-applied after a restart. `OnEffect` for Int32, Float32,
   Float3/`Vector3` keyed by the command's stable hash; handler exceptions logged, effect consumed.
   `NativeTreeRestartMode` Always/Never/OnSuccess/OnFailure, `Completed`/`Faulted` events,
   `Restart()`/`Stop()`. OnDisable stops and destroys the hidden host, unregisters from the world
   and releases the program. World: assigned or one shared `AIBT Native World`.
2. **Auto-Prepare.** `ProductionTreeAsset` records source GUID, source SHA-256 and catalog type
   (`RecordPreparation`, called by `ProductionTreeAssetBaker`). `Editor/Graph/
   ProductionTreeAssetStaleness.cs`: states Current/SourceChanged/CatalogChanged/SourceMissing/
   CatalogMissing/Unknown; re-prepares in place (GUID and references kept) on source import
   (synchronous postprocessor) and after script reload when the catalog fingerprint changed. The
   runtime asset inspector shows source, catalog and state with Prepare Now / Prepare Again /
   Open Source Graph. `NativeTreeAgentInspector` warns about a stale asset and shows a live section
   in Play Mode (status, finished runs, last result, Restart/Stop/Debug in Graph).
3. **Live debug.** `GraphLiveDebugger.Attach(NativeTreeAgent)`: reads the host activation stack
   (`ProductionTreeHost.TryCopyActiveNodes`, internal, via `InternalsVisibleTo("AIBT.Editor")`)
   and maps runtime node indexes through the program debug map. Debug Selected and selection
   auto-attach work for native agents.
4. **Rename.** Package shard `aibt.stdlib.standard-native` → `aibt.native.standard`; nodes
   `aibt.native.bool-is-true|float-at-least|set-float|wait-microseconds`. `aibt.stdlib.*` remains
   for dual-path nodes. Tests, MCP expectations, generated docs and CHANGELOG updated; shard
   visibility unchanged. `Planning~/Evidence/P9-002/README.md` keeps the old names as history.
5. **Sample.** `Samples~/FirstAgent` runs on `NativeTreeAgent`; the runtime asmdef references only
   `AIBT.Runtime`. Setup menu **AIBT > Samples > First Agent > Create Demo** prepares the runtime
   asset and saves a demo scene. The imported host copy `Assets/Samples/AIBT/First Agent/` was
   updated to the package files; demo scene `Assets/AibtFirstAgentDemo/FirstAgent.unity`
   (host project, not committed).

## Adapter size (First Agent gameplay script)

| | Lines | Non-blank | Wiring |
|---|---|---|---|
| Before (`HEAD`, `Runtime/FirstAgent.cs`) | 65 | 58 | `IProductionTreeWorldParticipant`, manual program/host/world, handle resolution, stable-hash effect matching, teardown order |
| After | 32 | 28 | `NativeTreeAgent.OnEffect`, `Completed`, `SetFact` |

## Verification

- `Invoke-UnityRecompile.ps1`: no compile errors. `Invoke-UnityTests.ps1` (host `Temp/stage4/
  tests-final.txt`): `AIBT.Runtime.Tests` 707/707, `AIBT.Editor.Tests` 615/617 before P9-005 and 616/618 in the final run `tests-final2.txt` (only the two
  pre-existing host-only failures `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`
  and `McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`),
  `AIBT.Integration.Tests` 69/69, `AIBT.BehaviorCases.Tests` 65/65. Generated documentation drift
  tests are green after regenerating `Documentation~/generated/api-reference-*.md`.
- `Tools~/Verification/Verify-Static.ps1`: passed (164 work items).
- Public API additivity: `Get-FullPublicApi.ps1 -BaselinePath` with the `HEAD` dump as baseline:
  0 removed or renamed, 41 lines added (`NativeTreeAgent`, `NativeTreeRestartMode`,
  `ProductionTreeAssetStaleness`, `ProductionTreeAssetState`, provenance properties and
  `FormatFingerprint` on `ProductionTreeAsset`). The committed script fails at `HEAD` already (it
  does not copy `Agents/` and `Analyzers/` into its isolated project), so it was run from scratch
  copies with those two `Copy-Item` lines added; the script itself was not changed.

- New tests: `Tests/Integration/NativeRuntime/NativeTreeAgentTests.cs` (8: facts reach the tree and
  survive restarts; typed effects by name, unhandled ignored; throwing handler logged, later
  effects delivered; restart policies; two agents share one program with separate state;
  deactivation stops hosts, leaves the world, disposes the shared program with the last agent and
  leaks no program; wrong fact type reported once, tree keeps running; explicit `CatalogFactory`).
  `Tests/Editor/Consumer/ProductionTreeAssetStalenessTests.cs` (6: provenance recorded and
  Current; edited source → SourceChanged → `RefreshAll` re-prepares in place; catalog fingerprint
  change → CatalogChanged → Prepare Again; legacy asset Unknown, catalog inferred; missing source
  leaves asset untouched; importing an edited source re-prepares synchronously).
- Play Mode via Unity CLI (demo scene, `eval_file` probe): after ~425 host updates the cube was at
  x = -2.65 moving to the goal at +5; after ~1488 updates `CompletedRuns = 1`, `LastResult =
  Success`, the goal had flipped to -5 and the cube was returning (x = -0.72). No new console
  errors from the Play session.
- Auto-Prepare in the Editor: changing `speed` in `first-agent.aibt.json` and importing logged
  `AIBT: re-prepared Assets/AibtFirstAgentDemo/FirstAgent.runtime.asset (source changed).` and the
  asset state became Current immediately.

## Captures (host `Temp/stage4/`, not committed)

- `first-agent-play.mp4` — 30 s Play Mode, 900 frames, stills in `first-agent-play.mp4.stills/`.
- `native-tree-agent-inspector.png` — agent inspector with the live section in Play Mode.
- `native-tree-agent-stale-warning.png` — agent inspector warning about a stale asset.
- `production-tree-asset-stale.png` — runtime asset inspector, SourceChanged.
- `production-tree-asset-auto-prepared.png` — the same asset after automatic re-preparation.
- `graph-live-native.png`, `graph-live-native.mp4` — graph window attached to the native agent.

## Not done / limits

- Live view: inner-node exit statuses, activations that start and end within one frame and native
  blackboard values are not shown; a host is not read while driving or with a pipelined job in
  flight. The trace channel was not used because it keeps only the first N records.
- The catalog is resolved by reflection from the recorded type name. Player builds with stripping
  need `CatalogFactory` or a preserved type; a Player build was not tested (P9-006).
- Running hosts keep the program they started with after an automatic re-preparation; restart
  them to pick up the new snapshot.
- Assets prepared before this change are `Unknown` until prepared again once.
