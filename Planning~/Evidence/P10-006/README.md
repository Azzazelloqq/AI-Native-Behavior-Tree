# P10-006 — AIBT on Unity 6000.6.9f1: compatibility fix and test baseline

Card: `Planning~/Tasks/P10/P10-006-unity-6000-6-compat.md`. Date: 2026-11-16.

**AIBT compiles on Unity 6000.6.9f1 after a two-line conditional-compilation change, and all eight
AIBT test assemblies give the same counts as the last 6000.7.0b1 baseline (no new failure).**

## 0. Context

Unity 6000.7.0b1 was uninstalled from the machine on 2026-11-16. The owner chose to migrate the
Modules host project to Unity **6000.6.9f1** (revision 8c45277fb9c5). 6000.7 can no longer be run
here, so the 6000.7 branch of each condition below is unchanged but **not re-verified**.

## 1. Package change (this commit)

`AssetDatabase.GetAssetPath(int)` is obsolete as an error (CS0619) in 6000.6.9f1. The importer
selected the `EntityId` overload only from 6000.7. The condition now starts at 6000.6:

| File | Change |
|---|---|
| `Editor/Graph/BehaviorTreeImporter.cs` (`OpenTree`) | `#if UNITY_6000_7_OR_NEWER` → `#if UNITY_6000_6_OR_NEWER` |
| `Tests/Editor/Consumer/GraphConsumerWorkflowTests.cs` (`OpeningAnUnrelatedAssetIsNotClaimedByAibt`) | same condition, so the reflection call passes an `EntityId` to the `EntityId` signature |

On 6000.7 and newer both conditions evaluate exactly as before. No runtime code, public API, ABI v2,
default or package version changed. The `EntityId` overload of `OpenTree` compiles on 6000.6.9f1 and
`OpeningAnUnrelatedAssetIsNotClaimedByAibt` passes there (it is part of the 635 passed in
`AIBT.Editor.Tests`).

## 2. Test results on 6000.6.9f1

Raw output: `tests-6000.6.9f1.txt` (EditMode through `Tools/Unity/Invoke-UnityTests.ps1`, PlayMode
through `run_tests --mode playmode --async_tests` + `test_status`).

| Assembly | 6000.7.0b1 (`659a8b1`/`7de8a69`) | 6000.6.9f1 (this card) | Difference |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 710/710 | 710/710 | none |
| `AIBT.Editor.Tests` | 635/636 | 635/636 | none (same failure) |
| `AIBT.Integration.Tests` | 88/88 | 88/88 | none |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | none |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | none |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | none |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | none |
| `AIBT.PlayMode.Tests` | 1/1 | 1/1 | none |

The one failure is the known host-only failure
`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`: the Modules host has
five sample nodes in its catalog (`aibt.samples.first-agent.move`, three `aibt.samples.swarm.*`,
`samples.simple.next-waypoint`), so the "built-in only" catalog has 74 entries instead of 69. It is
the same failure, with the same message, as on 6000.7.0b1.

## 2.1 Editor tooling on 6000.6.9f1 (visual check)

- `aibt-graph-guard-6000.6.9f1.png`: the AIBT Graph window opened `Samples/AIBT/Simple Agent/Trees/guard.aibt`
  through its `OnOpenAsset` handler (the `EntityId` overload changed above), status `Valid`, tree,
  blackboard and project tree list drawn (`Tools/Unity/Save-UnityWindow.ps1 -Window 'AIBT Graph' -OpenAsset …`).
- `simpleagent-playmode-t06s.png`, `simpleagent-playmode-t18s.png`: two stills of a 30 s Play Mode
  clip of `Assets/AibtSimpleAgentDemo/SimpleAgent.unity` (`Save-UnityClip.ps1 -Play -Seconds 30
  -StillsEvery 3`; the mp4 stays in the host's `Temp/migration/`, not committed). The guard, scout and
  wanderer move and the beacon changes state between the stills.
- Console during the session: no compile error; besides the stale-artifact messages (section 5) the
  Editor logged `Release of invalid GC handle. The handle is from a previous domain` six times from
  `AssetPostprocessingInternal.PostprocessAllAssets` after the forced reimport. It comes from the
  Editor's asset postprocessing, not from AIBT code, and was not investigated further.

## 3. Remaining warnings (not changed)

Same eight AIBT warnings as in `Planning~/Evidence/P10-005/README.md` section 0.1, plus three in
test code, all non-blocking. None was changed: each fix either touches runtime code or an attribute
member shape (outside this card), or changes behavior on 6000.7 as well.

| File | Warning | Why left |
|---|---|---|
| `Agents/Nodes/BtAttributes.cs:19` | CS0114 `BtNodeAttribute.TypeId` hides `Attribute.TypeId` | public attribute member; `new`/`override` is an API decision |
| `Agents/Runtime/ManagedNodeCatalog.cs:186` | UAC0005 `AppDomain.GetAssemblies()` | runtime discovery code |
| `Editor/Registry/GeneratedNodeReflectionHarness.cs:40`, `:144` | UAC0005 | discovery semantics |
| `MCP/Documentation/McpApiReferenceGenerator.cs:49` | UAC0005 | discovery semantics |
| `MCP/McpRuntimeTools.cs:163` (2) | CS0618 `FindObjectsByType(…, FindObjectsSortMode)` obsolete | the replacement overload would also apply on 6000.7; result order is part of MCP output |
| `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs:182` | CS0652 comparison outside `int` range | runtime code |
| `Tests/Editor/Performance/LargeGraphPerformanceTests.cs:73`, `:74` | CS0618 `ITransform.position/scale` obsolete | test code; also obsolete on 6000.7 |
| `Tests/Editor/Consumer/ShortFormDeclarationTests.cs:38`, `Tests/Editor/Documentation/McpDocumentationGeneratorsTests.cs:229` | UAC0005 | test code |

## 4. Host project (Modules, not committed, not part of this package)

Packages moved to the versions the 6000.6.9f1 Editor ships or requires (its
`Data/Resources/PackageManager/Editor/manifest.json` and `BuiltInPackages`):

| Package | 6000.7.0b1 | 6000.6.9f1 |
|---|---|---|
| `com.unity.render-pipelines.universal` | 17.7.0 | 17.6.0 (built in) |
| `com.unity.visualeffectgraph` | 17.7.0 | 17.6.0 (built in) |
| `com.unity.cinemachine` | 6.7.0 | 6.6.0 (built in) |
| `com.unity.timeline` | 6.7.0 | 6.6.0 (built in) |
| `com.unity.ugui` | 2.7.0 | 2.6.0 (built in) |
| `com.unity.test-framework` | 1.9.0 | 1.8.0 (built in) |
| `com.unity.collab-proxy` | 2.13.6 | 2.14.0 (Editor minimum) |
| `com.unity.ide.rider` | 3.0.39 | 3.1.0 (Editor minimum) |
| `com.unity.probuilder` | 6.1.2 | 6.1.4 (Editor minimum) |
| `com.unity.visualscripting` | 1.9.12 | 1.9.13 (Editor minimum) |
| `com.unity.graphtoolkit` | 0.5.0-exp.1 | unchanged (built in at the same version) |
| `com.unity.pipeline` | 0.7.0-exp.1 | unchanged (`unity` 6000.0; has 6000.5/6000.7 conditions) |
| `com.unity.addressables` | 4.0.1 | unchanged (`unity` 6000.0, above the Editor minimum 2.11.2) |
| `com.unity.cloud.gltfast` | 6.19.0 | unchanged (`unity` 6000.0, above the Editor minimum 6.14.1) |
| `com.unity.ide.visualstudio` | 2.0.27 | unchanged (above the Editor minimum 2.0.26) |
| `com.unity.modules.localizationruntime`, `com.unity.modules.smartstrings` | 1.0.0 | removed (do not exist in 6000.6.9f1) |

Burst and Collections are built in at the same version numbers as under 6000.7.0b1 (P10-005 §0).

Two host-tool fixes were needed outside the package: `Assets/DevTools/Editor/Capture/EditorWindowCapture.cs`
(`VisualElement.CaptureToRenderTexture` exists only from 6000.7; the `ui` capture method now throws on
older Editors, `screen`/`auto` work) and `Tools/Unity/Invoke-UnityRecompile.ps1` (Unity CLI 1.0.0
returns `recompile_status` as a JSON string; the script now decodes it, unrelated to the Unity version).
`Tools/Unity/Enter-UnityPlayMode.ps1` also needed a fix for the CLI, not for Unity: CLI 1.0.0 writes
a hint line to stderr, which Windows PowerShell 5.1 turns into a terminating error under
`$ErrorActionPreference = 'Stop'`. The `editor_play` call now runs under `Continue`.
`Start-UnityEditor.ps1` has the same `unity open … *> $null` pattern and was not run, so it was not changed.

## 5. Not verified

- Unity 6000.7: not installed any more; the unchanged 6000.7 branches were not compiled again.
- Player builds (Mono/IL2CPP) of the Modules project on 6000.6.9f1: not built in this card (the
  P10-005 bench built IL2CPP Players on 6000.6.9f1 from a copy of the package).
- Assets whose import results were written by 6000.7: the Editor logs
  `Failed to load 'VirtualArtifacts/Primary/<guid>' … serialized with a newer version of Unity` for
  them; 13 such assets were force-reimported (three sample `.aibt` trees, `SimpleAgent.unity`, URP/RP
  core meshes and scene templates, one timeline `.po`). Others may show the same message when first
  loaded; a full reimport (or a fresh `Library/`) clears it.
