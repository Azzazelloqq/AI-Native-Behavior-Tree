# P11-011 evidence — Consumer install and IL2CPP Player build (F14)

Run on 2026-11-23, 22:53 to 23:02. Unity 6000.6.9f1, Windows 11. All raw files are under
`C:\UnityProjects\_aibt_f14\`; the full report is `REPORT.md` there. This page summarises it and adds
the corrections of an independent read and where each defect was resolved (P11-015).

## Result

- The package installs from an immutable tarball.
- All 6 samples import through the Package Manager and compile without warnings.
- The three **Create Demo** menus work as documented.
- A Windows x64 IL2CPP release Player builds in about 2 minutes.
- In the Player, Simple Agent, First Agent and Native Guard run correctly in both runs, and Burst is
  enabled and in use.
- Swarm Arena's trees run in the Player, but in a Built-in project its scene is broken: magenta ground,
  no agents visible, and an exception every frame in a headless run. Half of its agents also advance
  only about 1/32 as often as the other half.
- MCP and CLI `validate` reject the Simple Agent trees in a project without `.aibt/policy.json`.

## Package

| | |
| --- | --- |
| Commit | `0aa9a6af57ea0a10348a0120f351ba1e13f95c54`: a planning-only commit on top of `0c2b541` (outside `Planning~`, `git diff --name-only 0c2b541 HEAD` is empty). The checkout was clean when archived. |
| Installed from | `pkg/com.azzazello.aibt-0.1.0.tgz`: 25.6 MB, SHA-256 `71071a38…`, 3061 files. `git archive HEAD` repacked under `package/` without the git pax header. |
| Package Manager | `source=LocalTarball`, `resolvedPath=Library/PackageCache/com.azzazello.aibt@e635ade2c7bd` (`out/import.txt`). |
| Dependencies | `package.json` asks for Burst `1.8.29` and Collections `6.5.0`; 6000.6.9f1 resolves its built-in Burst `2.0.0` and Collections `6.6.0`. Newtonsoft JSON `3.2.2` comes from the registry. |
| Writes into the package | None by AIBT. After every step, all 3061 installed files were compared by SHA-256 with the archive (`logs/packagecache-*.txt`). Only `package.json` differs, because the Package Manager appends `_fingerprint` while extracting. |

## Per sample

| Sample | Imported | User steps | Compiles | Diagnostics | IL2CPP Player |
| --- | --- | --- | --- | --- | --- |
| Simple Agent | 11 files | Create Demo: 0 errors | yes | tree list 3× Valid; MCP `validate` invalid (D1); importer status "Needs registry" (D6) | runs, both runs |
| First Agent | 10 files | Create Demo: 0 errors, prepared asset `Current` | yes | Valid; MCP valid (AIBT2053 info) | runs, both runs |
| Native Guard | 8 files | Create Demo: 0 errors, prepared asset `Current` | yes | Valid; MCP valid (AIBT2053 info) | runs, both runs |
| Swarm Arena | 14 files | open the scene, Play | yes | Valid; MCP valid | trees run; scene broken (D2, D3, D4) |
| Public Burst Nodes | 5 files | none | yes | no tree | no runtime part |
| Showcase Trees | 5 files | open in the Graph window (not automated) | n/a | Valid; MCP `validate` and `compile` succeed | no runtime part |

- Every sample asmdef produced a loaded assembly, with `scriptCompilationFailed=False`.
- Import and setup logged 0 warnings and 0 errors (`out/setup.txt`).
- MCP `compile` (reference compiler) fails with AIBT3012 for First Agent, Native Guard and Swarm Arena.
  This is expected: native-only nodes need `prepare_runtime`.

## Build

| | |
| --- | --- |
| Result | `Succeeded`, 0 errors, 2 warnings, 126 s inside `BuildPlayer` (`out/build.txt`) |
| Settings | IL2CPP (set by the driver), managed stripping Minimal, IL2CPP config Release, `BuildOptions.None`, .NET Standard 2.0 |
| Size | 90 MB shipped, `GameAssembly.dll` 26.4 MB; 713 MB with the IL2CPP backup folder |
| Burst | `lib_burst_generated.dll` 1.25 MB; in the Player `BurstCompiler.IsEnabled=True` and a probe job ran as Burst code |
| Stripping | no build message names AIBT; `AibtLinkXmlProcessor` passed `aibt-preserve.link.xml` to UnityLinker |
| The 2 warnings | missing scripts on the Swarm Arena scene's camera and light (D2) |

Player runs: 20 s per scene (`run/{windowed,batch}/f14-lines.txt`).

- **Windowed** (Direct3D12): exit code **0**.
- **Batch** (`-batchmode -nographics`): exit code **1**, only because Swarm Arena logged 1201 exceptions
  (D3).
- Both Players ended by themselves (84 s and 87 s).
- Screenshots: `run/windowed/shots/*.png`, 12 in all.

## Defects and where each went

The independent read of D1 to D9 (by the orchestrator, before P11-015 part B) corrected four of them.
Its corrections are marked **Correction**.

| | Defect | Where resolved or left |
| --- | --- | --- |
| D1 | MCP/CLI `validate` and `compile` reject the Simple Agent trees without `.aibt/policy.json` (12× AIBT2039 and 8× AIBT2040 per tree); the tree list calls them Valid. | Fixed in P11-015: `46da0de` (code and tests), `fee9937` (documentation, ADR-P9-001 amendment of 2026-11-24). |
| D2 | Swarm Arena's scene depends on URP, which the package does not require. | Fixed in P11-015 part A (`f1b5955`): a Create Demo menu replaces the shipped scene and makes materials of the project's pipeline. |
| D3 | Swarm Arena throws `InvalidOperationException: Instancing is not supported` every frame without a graphics device. | Fixed in P11-015 part A (`f1b5955`). |
| D4 | Swarm Arena's agents are invisible in a Built-in Player; the cause was likely but not verified. | Fixed in P11-015 part A (`f1b5955`): saved materials with instancing on keep the instanced variants (Standard `FORWARD` variants 8 to 16), and the agents are drawn in Built-in and URP. |
| D5 | The `PipelinedJobs` half of Swarm Arena advances about 1/32 as often as the Normal half; the cause was not determined. | Cause found and handled in P11-015: the sample applies its last decision every frame (`f1b5955`); the cadence is documented (`545f520`) and pinned by a test (`31ea067`). No scheduler change. |
| D6 | The `.aibt` importer status reads "Needs registry" for valid sample trees. | Documented in P11-015 (`545f520`, `BehaviorTreeSource.Status` and `Details`). Behaviour unchanged. |
| D7 | The README names versions other than a 6000.6.9f1 consumer gets. | Fixed in P11-015 (`545f520`). Open for the owner: `"unity": "6000.0"` in `package.json` is unverified below 6000.6. |
| D8 | The package ships 403 MB of planning evidence, one file of 370 MB. | Fixed in P11-015 (`bfadf82`): export-ignore and `.npmignore`, and the file left the tree. Open for the owner: the blob stays in history from `abca014`, and GitHub refuses files over 100 MB. |
| D9 | `consumer-quickstart.md` says the Player never reads JSON or compiles trees; Swarm Arena does. | Fixed in P11-015 (`545f520`). |
| new | Three test fixture assemblies (`AIBT.SampleCustomTool`, `AIBT.NodeDevelopment.ShardFixture`, `AIBT.NodeDevelopment.CatalogFixture`) were loaded in the consumer project (`out/setup.txt`), so every consumer exposed the fixture MCP tool `aibt_custom_sample_write_marker`. Found when P11-015 read this run's output. | Fixed in P11-015 (`caef926`). |

Corrections of the independent read:

- **Correction to D2.** The scene references URP's `Lit.mat` (guid `31321ba15b8f8eb4c954353edc038b1d`)
  **three** times, at lines 180, 525 and 600 of `SwarmArena.unity` at `0aa9a6a`, not twice. Its
  camera and light also carry URP's additional-data scripts (guids `a79441f3…` and `474bcb49…`), which
  are the 2 missing scripts.
- **Correction to D5: the cause.** A `PipelinedJobs` group advances one lifecycle step per frame, so it
  makes one logical update per (lifecycle steps of its slowest tree + 1) frames. Swarm Arena's tree
  takes about 31 steps. P11-015 part A measured 31.86 frames per update on the unchanged sample (26 to
  39 frames) and 31.2 to 31.9 afterwards. The sample moved an agent only when a move effect arrived, so
  the pipelined half barely moved. `performance.md` described the cadence as "about once every three
  frames", which holds only for a tree waiting in one leaf.
- **Correction to D6: not visible in the normal UI.** `BehaviorTreeSource` stores the status in
  `[HideInInspector]` fields, and the tree asset's inspector does not show it. A user meets it only
  through a script or a debug inspector; this run read it through the harness. The defect is a
  confusing public value, not a visible contradiction.
- **Correction to D9: wider.** It is not only Swarm Arena. Every `BehaviorTreeAgent`, including the
  Simple Agent sample, parses its tree's JSON and compiles it in the Player when it first loads it. The
  sentence holds only for `NativeTreeAgent`.

## Not checked

- Other platforms (only Windows x64), Mono, development builds.
- Stripping levels above Minimal (the README's "verified with High" comes from P9-006).
- A registry or git-URL install.
- URP projects (later checked in P11-015 part A).
- A project with Active Input Handling set to Input System only (later checked in P11-015 part A).
- Showcase Trees in the interactive Graph window; live graph debugging; hot reload and re-prepare after
  editing a tree.
- Swarm Arena's 256 and 512 presets and keyboard controls.
- Performance: frame times on a loaded machine are not evidence.

## Raw files (`C:\UnityProjects\_aibt_f14\`)

| Area | Files |
| --- | --- |
| Report | `REPORT.md` |
| Package | `pkg/COMMIT.txt`, `pkg/com.azzazello.aibt-0.1.0.tgz`, `pkg/src/` |
| Harness | `harness/Editor/F14Driver.cs`, `harness/Runtime/F14Bootstrap.cs`, `scripts/Invoke-F14.ps1` (the F14 originals are in `backup-f14/`; P11-015 extended them) |
| Logs | `logs/{Import,Setup,Build,RunBatch,RunWindowed,Verify}-result.txt`, `logs/*-editor.log`, `logs/packagecache-*.txt` |
| Driver output | `out/{import,setup,build}.txt`, `out/build-messages.txt`, `out/build-files.txt` |
| Player runs | `run/{batch,windowed}/{Player.log,f14-lines.txt}`, `run/windowed/shots/*.png` |

- Nothing was written in the Modules project, and no process was killed.
- The submodule had another session's uncommitted edits after 22:58. The tarball was made at 22:53
  from the clean `0aa9a6a` and contains none of them.
