# P11-015 evidence — Consumer-install fixes

Branch `feature/aibt-consumer-workflow`, base `9a395a1`. The briefs are in
`C:\UnityProjects\_aibt_acceptance\P11-015\`: `brief-a-swarm-arena.md`, `brief-b-consumer-install.md` and
`brief-b-item-8-size.md`.

- Raw files of part B: `C:\UnityProjects\_aibt_acceptance\P11-015\partB\`.
- Temp projects, builds and Player runs: `C:\UnityProjects\_aibt_f14\`. Part A's report is
  `REPORT-swarm.md` there.
- Environment: Unity 6000.6.9f1 and the Modules Editor (Pipeline port 7810) for the package's tests; the
  temp projects ran in the run's own batch-mode instances.
- The machine is loaded; no conclusion is drawn from timings.
- Only the touched test classes were run, not the ten assemblies.

Status: `in_progress`, waiting for the orchestrator's acceptance run.

## Commits

| Commit | Item | Content |
| --- | --- | --- |
| `46da0de` | 1 (D1) | fix(mcp): validate, compile and the manifest use the Editor's defaults without a policy file |
| `fee9937` | 1 (D1) | docs(policy): what happens without `.aibt/policy.json`; ADR-P9-001 amendment |
| `caef926` | 2 | fix(tests): fixture assemblies are test assemblies, so a consumer no longer compiles them |
| `f1b5955` | 3 (part A) | feat(samples): Swarm Arena works in a consumer project |
| `31ea067` | 4 (D5) | test(scheduling): pin the `PipelinedJobs` cadence by counting frames and steps |
| `545f520` | 4 (D5, D6, D7, D9) | docs: the cadence, the importer status, the verified Editor and install routes, where the Player compiles |
| `bfadf82` | 5 (D8) | chore(packaging): development folders and the 370 MB raw report leave the package |
| `a69c3f9` | CHANGELOG | consumer-install fixes |
| `3a1f061` | 6 | task cards, work items, MASTER_PLAN, P11-011 evidence, this evidence up to item 6 |
| `baa579e` | 7 | docs(readme): the `git archive` step writes the tar outside the checkout |
| `9ccabed` | 8 | docs(performance): size and memory; a CHANGELOG line |
| this commit (last) | 7, 8 | this evidence: the fresh-install proof and the size measurements |

## Part A: Swarm Arena (accepted by the orchestrator)

It was done in the P11-011 temp projects with the package from the `0aa9a6a` tarball. The full report
is `C:\UnityProjects\_aibt_f14\REPORT-swarm.md`; the delivered folder was `swarm-fix\SwarmArena\`.

- **Create Demo replaces the scene.** **AIBT > Samples > Swarm Arena > Create Demo**
  (`Editor/SwarmArenaSetup.cs`) builds `Assets/AibtSwarmArenaDemo/SwarmArena.unity`. It saves materials
  of the active pipeline: Standard in Built-in, `Universal Render Pipeline/Lit` in URP. The shipped
  `SwarmArena.unity`, which needed URP, is gone (D2).
- **Presenter.** It returns at once on `GraphicsDeviceType.Null` (D3). It falls back to
  `Graphics.RenderMesh` when instancing is not supported. It draws with saved materials that have
  instancing on (D4): the Built-in build kept 16 Standard `FORWARD` variants instead of 8.
- **Input.** The keys need the legacy Input Manager (`#if ENABLE_LEGACY_INPUT_MANAGER`). Without it the
  overlay says the keys are unavailable.
- **D5.** Pipelined agents apply their last decision every frame. Before the change, the pipelined
  cohort made one update per 31.86 frames, moved at 0.085 m/s and reached 0 of 32 patrol points.
  Afterwards it moved at 1.92 to 2.32 m/s and reached 32 of 32, with the same 31.2 to 31.9 frames per
  update. No scheduler change.
- **Runs.** All four final runs passed all 4 scenes with exit code 0, 0 errors and 0 warnings: Built-in
  windowed and batch, URP windowed and batch.

| Run | FPS | Pipelined frames per update | Pipelined speed | Patrol points reached, Normal / Pipelined | Exit |
| --- | --- | --- | --- | --- | --- |
| before, batch, Built-in | 60 | 31.9 | 0.085 m/s | 32 / 0 | 1 (1561 × "Instancing is not supported") |
| final, batch, Built-in | 60 | 31.2 | 2.32 m/s | 32 / 32 | 0 |
| final, windowed, Built-in | 225 | 31.9 | 1.94 m/s | 32 / 32 | 0 |
| URP, windowed | 235 | 31.7 | 1.92 m/s | 32 / 32 | 0 |
| URP, batch | 60 | 31.2 | 2.32 m/s | 32 / 32 | 0 |

Raw part A files:

- `run/{before-batch,final-windowed,final-batch,urp-windowed,urp-batch}/{Player.log,f14-lines.txt}`;
- `run/final-windowed/shots/SwarmArena-*.png` and `run/urp-windowed/shots/SwarmArena-*.png`;
- `out/{final,urp}/setup.txt`.

## Part B, per item

### 1. MCP and the CLI agree with the Editor without a policy file (D1)

**Before.** `validate` and `compile` used the default `ValidationOptions`, which allow neither managed nor
main-thread nodes, when `<project>/.aibt/policy.json` was missing:

- the Simple Agent trees got 12× AIBT2039 and 8× AIBT2040 each;
- `prepare_runtime` refused a native tree with Agent-scope keys (`valid:false`);
- `get_project_manifest` returned only an error object;
- an unreadable policy file was dropped silently.

**After:**

- **Defaults.** Without the file, `validate`, `compile` and `prepare_runtime` take their options from
  `ProjectNodeCatalog.DefaultPolicy(document, registry)`. That is the helper the tree list
  (`TreeCatalogEntry`) now also calls, not a third copy. A tree with managed nodes gets the managed
  layer's policy (`ManagedTreeProgram.Policy`). Any other tree gets the native default: Agent and Shared
  scope allowed. Each is applied to the tree after the observer rule's expansion.
- **The manifest.** `get_project_manifest` lists the trees and capabilities with `"policy": null`
  (`ProjectManifestQuery.WithoutPolicy`, internal).
- **An unreadable file.** It is reported as warning `AIBT9008` with the parser's message, which names the
  field (for example "missing required boolean field 'supportsSharedScope'"). The warning is in the
  `diagnostics` of `validate` and `compile` and in a new `policyWarnings` array of the manifest.
- **With a readable file** nothing changes, and `policyApplied` stays `true`.
- **Content hash.** A `compile` without the file has a different `contentHash` than before, because the
  program header records the policy. The test that compares MCP with a direct compiler call now passes
  the same default.

Schema (`Schemas~/project-manifest.schema.json`):

- Changes: `policy` is `oneOf` the summary or `null`, and `policyWarnings` is optional.
- Corrected along the way: tree items gain the optional `interface` and `referencedTreeIds` the tool
  already returned.
- No version bump. `data-formats.md` states its versioning rules for persisted formats only, and this
  is a response schema. The change adds a value and an optional field, so every response that was valid
  stays valid.
- Checked with `check-jsonschema 0.38.0` against a synthetic no-policy document and both P7-019
  examples: all pass.

Documentation and decision:

- `data-formats.md`, the project policy section: what the tools do without the file, what the file is
  for, that every field is required, and a minimal file.
- `cli.md` and `ai-and-mcp.md` link to that section.
- ADR-P9-001 has a dated amendment (2026-11-24): what the default is and why the two tools changed.

Tests: `McpVerificationToolDispatcherTests` and `McpToolDispatcherTests` (Discovery).

| Run (`partB\tests\`) | Verification | Discovery | Note |
| --- | --- | --- | --- |
| `item1-before.txt` | 24/30, 6 fail | 8/10, 2 fail | The new tests fail before the fix: the three Simple Agent trees, Agent scope without and with a policy, the unreadable file, `prepare_runtime`, and the manifest without a file and with an unreadable one. |
| `item1-after.txt` | 29/30 | 10/10 | The one failure was the content-hash comparison, fixed in the test (see above). Also run and passing: `SubtreeImportAndBakeTests` 6/6, `TreeListScanTests` 2/2, `ProjectManifestQueryTests` 3/3, `GraphEditorWorkflowTests` 15/15. |
| `item1-after-2.txt` | 30/30 | – | |
| `item1-after-3.txt` | 33/33 | 10/10 | Three more tests: a managed tree the agent would refuse is still refused, a project with a policy keeps today's answers, and the Simple Agent trees are valid under the minimal policy the documentation shows. |

### 2. Test fixture assemblies no longer compile into a consumer's Editor

- **The three assemblies.** `AIBT.SampleCustomTool`, `AIBT.NodeDevelopment.ShardFixture` and
  `AIBT.NodeDevelopment.CatalogFixture` were not test assemblies. They were loaded in the P11-011
  consumer project (`C:\UnityProjects\_aibt_f14\out\setup.txt`), and the first one contributes the MCP
  tool `aibt_custom_sample_write_marker`.
- **The fix.** All three now carry `"optionalUnityReferences": ["TestAssemblies"]`, as the other
  Editor test asmdefs do.
- **The other asmdefs.** All 22 other asmdefs under `Tests/` already were test assemblies: the
  `TestAssemblies` reference, or `UNITY_INCLUDE_TESTS` for PlayMode.
- **The guard.** `TestAssemblyIsolationTests` (new) checks every asmdef under `Tests/`.
- **Results.** `item2-before.txt`: 0/1, naming the three. `item2-after.txt`:
  - `TestAssemblyIsolationTests` 1/1;
  - `McpCustomToolsToolDispatcherTests` 7/7;
  - `McpNodeDevelopmentToolDispatcherTests` 34/34;
  - `ProjectLeafRegistrationTests` 11/11.

### 3. Swarm Arena ported

- **The copy.** `swarm-fix\SwarmArena\` was copied over `Samples~/SwarmArena`, and `diff -r` shows the
  two identical. `SwarmArena.unity` and its meta were removed. `Editor/SwarmArenaSetup.cs` was added
  with its delivered GUID `df418397…`. `package.json` is unchanged.
- **The Modules Editor.** Its recompile reported nothing new: no duplicate-GUID or Swarm Arena message.
  The Modules project's own copy `Assets/SwarmArena/` was not touched.
- **The danger zone's purple is its intended colour**, (0.85, 0.2, 0.75), not the error shader:
  - Built-in (`C:\UnityProjects\_aibt_f14\out\final\setup.txt:85`):
    `Material:Danger-Zone@Assets/AibtSwarmArenaDemo/Danger-Zone.mat{shader=Standard,instancing=False}`;
  - URP (`out\urp\setup.txt:86`): shader `Universal Render Pipeline/Lit`.

### 4. Documentation

- **D5.**
  - `performance.md`: a new paragraph, anchor `pipelinedjobs-cadence`. It states the rule (one logical
    update per lifecycle steps of the slowest tree + 1 frames), the group (one tree asset, one profile)
    and the measured cadences.
  - The pipelined rows of the page are requalified. They are the cost of a frame in which an agent does
    a fraction of an update, not the cost of an update. Per logical update the cost is about 2.9 × 3 ≈
    8.7 µs against 3.8 µs for `io-wait1`, and about 2.6 × 6.6 ≈ 17 µs against 8.3 µs for
    `shared-loop16`, at N=1 000. The sentence "a third of the work per frame" was the only other place
    that rested on the old reading, and it is gone.
  - `execution-and-scheduling.md:19` (the table row) is rewritten.
  - The P11-014 Known limitations entry links to the new text.
- **The cadence test** is `PipelinedJobsCadenceTests` (`31ea067`). It counts frames and lifecycle steps
  per logical update of forced-pipelined hosts and does not time anything.

  | Case | Frames per update | Steps per update |
  | --- | --- | --- |
  | One waiting leaf | 3 | 2 |
  | A longer reactive sequence | 28 | 27 |
  | `shared-loop16` from `Benchmarks~` | 3, 26 or 44 (mean 6.56 over 120 frames) | 2, 25 or 43 |
  | A two-path tree, one asset: the short path alone, the long path alone, then both | 17, then 32, then 32 for both | 16, then 31 |
  | Two assets on one profile | the one-leaf host stays at 3 next to the 28-frame tree | separate groups |

  - Runs: `item4-cadence-{1..4}.txt`. The final run is 5/5, and its log lines are in
    `item4-cadence-4-log.txt`.
  - My first group test used two different assets and failed (`item4-cadence-1.txt`). The test was
    wrong: groups are per program. So the group test now uses one asset, and a separate test pins the
    separate groups.
- **D6.** The comments of `BehaviorTreeSource.Status` and `Details` say what the value is: the
  import-time check against the built-in registry only. Project nodes are validated by the inspector and
  the Graph window. No behaviour change.
- **D7.**
  - `README.md` says the package is verified on `6000.6.9f1`, and that an Editor with newer built-in
    Burst and Collections resolves those (`2.0.0` and `6.6.0` there).
  - `package.json` is unchanged. **Open for the owner:** `"unity": "6000.0"` is unverified for every
    Editor before `6000.6`, which is wider than the brief's "before 6000.5", since nothing older than
    `6000.6.9f1` was run.
- **D9.** `consumer-quickstart.md:172`: the sentence holds for `NativeTreeAgent`. A `BehaviorTreeAgent`
  and Swarm Arena's low-level path parse and compile in the Player. `Samples~/FirstAgent/README.md:33`
  is qualified the same way.
- **Install routes.** `README.md` gives the two verified routes with exact steps: a local checkout
  through `file:`, and a tarball made with `git archive`. A git URL was not exercised: the repository's
  published state is behind.
- **Results.** `item4-after.txt`: `UserGuideSnippetTests` 6/6, `McpDocumentationGeneratorsTests` 14/14,
  `PipelinedJobsCadenceTests` 5/5.

### 5. Packaging (D8)

- **The raw report.** `Planning~/Evidence/P7-035/swarm-arena-player-20260917-090844.raw.json`:
  370,292,632 bytes, SHA-256 `94af8a809094fd0d1194514b45ec64e13f193b7a69d77e715d856e4e7608768d`, blob
  `eba55a92…`.
  - It was copied to `C:\UnityProjects\_aibt_acceptance\P7-035\`. Copy and original had the same SHA-256
    before `git rm`.
  - The P7-035 README says where it is now and how to restore it:
    `git show 545f520:Planning~/Evidence/P7-035/swarm-arena-player-20260917-090844.raw.json`.
- **Readers of that file**, searched before the removal:
  - the P7-035 README, which names it;
  - `work-items.json`, whose `owns` names only the folder;
  - `Verify-Static.ps1`, which parses tracked JSON, so it has one file less to parse;
  - no test and no benchmark script, since those write their own output.
- **`.gitattributes`** gains `export-ignore` for `Planning~/**`, `Benchmarks~/**` and `Spikes~/**`.
  `Tools~` stays: `cli.md` tells consumers to run `Tools~/aibt.ps1`, and `development-commands.md` names
  `Tools~/Verification`.
- **`.npmignore`** lists the same folders plus a copy of `.gitignore`, because npm reads `.npmignore`
  instead of `.gitignore`.

| `git archive` (`partB\packaging\`) | Files | Tar bytes | Gzip bytes | Dev-folder files |
| --- | --- | --- | --- | --- |
| before, `545f520` (`archive-before-545f520.*`) | 3098 | 422,819,840 | 25,463,038 | `Planning~` 403,485,634 bytes |
| after, `bfadf82` (`archive-after-bfadf82.*`) | 2133 | 14,284,800 | 2,385,697 | 0 |

- **Other differences.** Apart from the excluded folders, the only change in the file list is the added
  `.npmignore`. `Tools~` keeps its 47 files.
- **`npm pack --dry-run`** (npm was already installed; `npm-pack-dry-run.json`, `npm-pack-files.txt`):
  2128 entries, 2,371,913 bytes packed and 12,328,921 unpacked. It differs from the archive only by the
  `.gitignore` and `.npmignore` files, which npm drops. Its `Tools~` has 44 files.
- **A git-URL install was not tested.**
- **Open for the owner:** the blob stays in history from `abca014` (the push block recorded on
  2026-11-23). GitHub refuses files over 100 MB, so a push needs a history rewrite or an LFS migration
  first. No action here.

### 6. Planning records

- **P11-011.** Card `Planning~/Tasks/P11/P11-011-consumer-install-player-build.md`, work item `review`,
  evidence `Planning~/Evidence/P11-011/README.md`. The evidence adds the four corrections of the
  independent read and where each defect went.
- **P11-015.** This card and evidence, work item `in_progress`.
- MASTER_PLAN sentences for both, and the CHANGELOG section (`a69c3f9`).

### 7. Proof on a fresh install

Raw files: `C:\UnityProjects\_aibt_f14\fresh\`. The harness is the P11-011 one with one more step
(`McpCheck`, `F14Driver.McpChecks`) and a `-Root` parameter. The part A versions are in
`C:\UnityProjects\_aibt_f14\backup-partA\`.

**A new defect: the README's tarball step wrote outside its folder.** The README's first command
(`git -C <AIBT> archive … -o aibt.tar HEAD`) wrote `aibt.tar` **into the AIBT checkout**, because
`git -C` resolves a relative `-o` path there. The next command then found no file (`tar.exe: Error
opening archive`).

- The stray file was moved out of the checkout to
  `fresh\pkg\stray-aibt.tar-written-into-the-checkout`, and the checkout is clean again.
- Fixed in `baa579e`: the step now passes `-o "$PWD/aibt.tar"` and says why.
- The tarball below was made from `baa579e` with the corrected README steps, verbatim.

| | First run (P11-011, `0aa9a6a`) | Fresh install (`baa579e`) |
| --- | --- | --- |
| Tarball | 25.6 MB | **2.36 MB** (2,479,308 bytes, SHA-256 `91969ac8e399d1009fd21f66d1641d874a58eccbfb7431017b2f721c6d7cf97c`) |
| Files | 3061 | **2133** (12,319,461 bytes unpacked; 0 files of `Planning~`, `Benchmarks~` or `Spikes~`) |
| Installed copy after every step | 1 file changed (`package.json`, the Package Manager's `_fingerprint`) | the same: 2133 = 2133, 0 added, 0 removed, 1 changed (`logs\packagecache-*.txt`) |

`Planning~` is export-ignored, so `git archive` of `3a1f061` and of `baa579e` differ only in
`README.md`.

The project:

- `fresh\project\F14Fresh`, Built-in render pipeline, Unity 6000.6.9f1, with no `.aibt/policy.json`
  (`out\mcp.txt`: `POLICY_FILE|…|exists=False`).
- Burst `2.0.0`, Collections `6.6.0` and Newtonsoft `3.2.2` resolved (`out\import.txt`).

**Import and setup** (`out\import.txt`, `out\setup.txt`):

- All 6 samples imported, with 0 warnings and 0 errors.
- The 4 Create Demo menus ran with 0 errors and 0 warnings each, including Swarm Arena's new one.
- Both prepared assets are `Current`.
- The tree list shows all 8 trees Valid.

**Loaded AIBT assemblies** (`out\mcp.txt`, 23): `AIBT.Agents`, `AIBT.Authoring`,
`AIBT.Authoring.BuiltInLeaves`, `AIBT.Authoring.BuiltInLeaves.Catalog`, `AIBT.BurstNodes.Sample`,
`AIBT.BurstNodes.Sample.Catalog`, `AIBT.Editor`, `AIBT.Mcp`, `AIBT.Runtime`, `AIBT.StandardNodes`, and
the 13 sample assemblies (`AIBT.Samples.*`).

- `FIXTURE_ASSEMBLY|…|loaded=False` for all three fixture assemblies.
- 0 AIBT assemblies have "Test" or "Fixture" in their name. P11-011 loaded the three fixtures.

**MCP** (`out\mcp.txt`; raw responses in `out\mcp-list-operations.json`,
`out\mcp-list-custom-tools.json` and `out\mcp-project-manifest.json`):

- **Tool list.** `list_operations` gives 44 operations and `list_custom_tools` gives
  `{"tools":[],"diagnostics":[]}`, so `aibt_custom_sample_write_marker` is absent.
- **Manifest.** `get_project_manifest` lists all 8 trees with `"policy": null` and no `policyWarnings`.
  That real response validates against `Schemas~/project-manifest.schema.json` (`check-jsonschema
  0.38.0`: ok).
- **`validate` without a policy file:** all 8 trees are `valid:true, policyApplied:false`. Native
  Guard, First Agent and Swarm Arena have only AIBT2053 info diagnostics; the other five have none.
- **`validate` under the minimal policy of `data-formats.md`:** the harness writes the documentation's
  JSON block into `.aibt/policy.json`. All 8 trees are `valid:true, policyApplied:true`, so the
  documented claim holds. `get_project_manifest` then reports that policy. The file was removed
  afterwards.
- **A side observation**, open finding below. The first run of this step (`out\mcp-attempt1-before-catalog-refresh.txt`)
  called MCP straight from `-executeMethod` in a fresh Editor process. There, `validate` reported AIBT2021
  ("Node type is not present in the selected registry") for every project node: the Simple Agent
  trees, First Agent, Native Guard and Swarm Arena.
  - The cause: `ProjectNodeCatalogCache` starts with the built-in nodes and discovers project nodes on
    `EditorApplication.delayCall`, which has not run yet when an `-executeMethod` entry point starts.
  - The Setup step's earlier MCP calls ran after the Create Demo menus had refreshed the catalog, and
    they were valid.
  - The harness now calls the cache's `Refresh` first, which is what `delayCall` does in an interactive
    Editor.

**Player** (`out\build.txt`, `logs\Build-result.txt`):

- Windows x64 IL2CPP release, managed stripping Minimal.
- `Succeeded`, 0 errors, **0 warnings** (P11-011: 2, the missing URP scripts), 128 s.
- 93.7 MB shipped without the IL2CPP backup folder; `GameAssembly.dll` 26.5 MB; `lib_burst_generated.dll`
  1.25 MB.

| Run (`run\…\f14-lines.txt`) | Burst | Simple Agent | First Agent | Native Guard | Swarm Arena | Warnings | Exit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| batch (`-batchmode -nographics`) | enabled, probe job ran as Burst | PASS (Guard 60, Scout 54, Wanderer 10 completions; 3× `spotted`) | PASS (3 runs) | PASS | PASS (64/64 updated, pipelined 31.22 frames per update, 32/32 patrol points) | 0 | **0** (90 s) |
| windowed (Direct3D12) | enabled, probe job ran as Burst | PASS | PASS | PASS | PASS (pipelined 31.5 frames per update, 32/32) | 0 | **0** (92 s) |

- Neither `Player.log` contains the word "exception" (0 matches each).
- Screenshots: `run\windowed\shots\*.png`, 3 per scene. Swarm Arena's agents are drawn in state colours
  and the danger zone is purple.
- `logs\Verify-result.txt`: the submodule's `git status --short` is empty and its head is `baa579e`.
- **Not repeated:** the URP copy. Part A ran the same Swarm Arena files under URP (windowed and batch,
  exit code 0), and nothing in part B touches rendering.

### 8. Size and memory

Brief: `brief-b-item-8-size.md`. Measured in temp projects only; no package code changed.

**Conditions.** Windows 11 x64, Unity 6000.6.9f1, Standalone Windows x64, IL2CPP, release (not
development), Built-in render pipeline, the package from the tarball of item 7 (`baa579e`). Four
projects under `C:\UnityProjects\_aibt_f14\size\project\`, each with the same probe scene
(`harness\Runtime\SizeProbe.cs`, one GameObject and a camera):

- **A**: no AIBT;
- **B**: AIBT and the First Agent sample (a native-only game: `NativeTreeAgent`, no managed tree);
- **C**: AIBT and the Simple Agent sample (a managed-only game: `BehaviorTreeAgent`, no native tree);
- **D**: AIBT, First Agent, Simple Agent, Native Guard and the generated trees. Only for growth and
  memory; StandardNodes comes with Native Guard, so D is not a size reference.

Each build's scene list is the probe scene plus the sample's demo scene (D: the Memory scene). Managed stripping `Minimal`
unless named; B also with `High`. Scripts: `scripts\Invoke-Size.ps1` (steps) and
`scripts\Invoke-SizeChain.ps1`; sizes by `tools\build_sizes.py` (`raw\build-sizes.txt`).

**Not measured:** other platforms (Android, iOS, consoles, WebGL), the Mono backend, development
builds, mobile, URP or HDRP, Burst-disabled builds, compressed downloads (installer, store package).

#### 8.1 What AIBT adds to a Player

| Build | Shipped bytes | Δ to A | `GameAssembly.dll` | Δ to A | `lib_burst_generated.dll` | `_Data` | AIBT IL (assemblies) | AIBT C++ generated |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A, no AIBT, Minimal | 69,491,211 | – | 8,764,416 | – | – | 12,869,251 | – | – |
| B, native only, Minimal | 92,052,652 | +22,561,441 | 26,064,896 | +17,300,480 | 388,608 | 18,130,212 | 1,569,792 (8) | 31,945,533 |
| B, native only, High | 84,669,964 | +15,178,753 | 20,613,120 | +11,848,704 | 369,664 | 16,199,300 | 869,888 (8) | 17,630,983 |
| C, managed only, Minimal | 91,959,937 | +22,468,726 | 26,037,248 | +17,272,832 | 313,344 | 18,065,145 | 1,545,216 (6) | 31,820,321 |

- "Shipped" is every file of the build folder except `SizePlayer_BackUpThisFolder_ButDontShipItWithYourGame`.
  IL is the stripped IL that il2cpp compiled (`…BackUpThisFolder…\Managed`); C++ is
  `…BackUpThisFolder…\il2cppOutput`, files named after AIBT assemblies (the shared `Generics*` files are
  not attributed, so the C++ column is a lower bound).
- `_Data` grows by 5.3 MB in B and C: `il2cpp_data` (the IL2CPP metadata) from 3,054,253 to 7,775,464
  bytes in B (+4.7 MB), and `Plugins\x86_64\lib_burst_generated.dll` (0.39 MB in B, 0.31 MB in C). The
  tree assets themselves are a few KB (8.2). `Resources` is 6,453,444 bytes in every build.
- Per assembly, B Minimal / B High IL: `AIBT.Runtime` 934,912 / 523,264, `AIBT.Authoring` 486,400 /
  230,400, `AIBT.Agents` 89,088 / 82,944, built-in leaves and catalog 28,672 / 14,848, the sample's own
  three assemblies 30,720 / 18,432. C++: `AIBT.Runtime` 17.7 / 10.7 MB, `AIBT.Authoring` 12.6 / 5.3 MB,
  `AIBT.Agents` 1.39 / 1.35 MB.

**In words.**

- A native-only game (B) pays 22.6 MB shipped with Minimal stripping, of which 17.3 MB is
  `GameAssembly.dll`, 0.39 MB Burst code and 5.3 MB `_Data`.
- A managed-only game (C) pays 22.5 MB, almost the same. Both Players hold `AIBT.Runtime`,
  `AIBT.Authoring` and `AIBT.Agents`: the package's runtime assemblies are auto-referenced, so every
  build takes them whether a scene uses them or not, and Minimal stripping leaves them whole.
- **`AIBT.Authoring` is in a native-only Player:** 486,400 bytes IL, 12.6 MB C++ with Minimal.
- **High stripping does not remove it:** 230,400 bytes IL, 5.3 MB C++ and 153 types remain, among them
  `NodeRegistry`, `ReferenceCompiler`, `CanonicalTreeJson` and the subtree expansion: the managed compile
  path. Under High `AIBT.Agents` keeps `BehaviorTreeAgent`, `BehaviorTreeSource` and all 60 built-in
  managed nodes (93 types) although no scene of B uses them, and `BehaviorTreeAgent` compiles through
  `AIBT.Authoring` (D9). The package has no `link.xml` and no `[Preserve]`; why the linker keeps these
  types was not traced. High saves 7.4 MB shipped in B (22.6 → 15.2 MB over A).
- **The Player runs under High:** B High, First Agent PASS (133,395 host updates in 12 s), exit 0,
  no error logged (`run\B-High-scene\size-lines.txt`). B Minimal PASS (97,678 updates), C Minimal PASS
  (Guard 1, Wanderer 5, Scout 40 completions), A exit 0. Not checked under High: C (managed trees use
  reflection-created nodes, the riskier case for High stripping).

#### 8.2 Growth with tree size

Generated trees (`tools\generate_trees.py`, `raw\generated-trees.txt`): the sample's branches repeated
under its root with suffixed ids. Native trees use the Native Guard catalog's leaves, managed trees the
Simple Agent nodes. The native ones are baked with `ProductionTreeAssetBaker.Create` and the Native Guard
catalog, the managed ones compiled with `ReferenceCompiler` (`SizeAibtDriver.PrepareGrowth`,
`out\D\growth.txt`); breakdowns by `tools\payload_breakdown.py`
(`raw\generated-native-breakdown.txt`).

Native path (`NativeTreeAgent`, prepared asset):

| Tree | Nodes | Source bytes | Semantic program bytes | Payload bytes | Asset on disk | Asset in the build ("Used Assets") |
| --- | --- | --- | --- | --- | --- | --- |
| `size-native-10` | 15 | 3,858 | 2,978 | 6,861 | 14,669 | 7.1 KB |
| `size-native-100` | 109 | 23,679 | 18,592 | 39,547 | 80,043 | 39.0 KB |
| `size-native-1000` | 1009 | 214,975 | 169,797 | 356,807 | 714,565 | 348.9 KB |
| per node, 100 → 1000 | | 213 | 168 | 353 | 705 | 353 |

Managed path (`BehaviorTreeAgent`, `BehaviorTreeSource`):

| Tree | Nodes | Source bytes | Reference program bytes (compiled in the Player) | JSON the asset carries | Asset in the build ("Used Assets") |
| --- | --- | --- | --- | --- | --- |
| `size-managed-10` | 12 | 3,407 | 2,367 | 3,407 | 3.7 KB |
| `size-managed-100` | 108 | 25,165 | 17,169 | 25,165 | 25.0 KB |
| `size-managed-1000` | 1004 | 230,700 | 157,847 | 230,700 | 225.8 KB |
| per node, 100 → 1000 | | 229 | 157 | 229 | 230 |

- "Used Assets" is the Editor log's build report of the rebuilt D Player (`out\D\Minimal-r2-editor-build-report.txt`,
  KB = 1024 bytes); the first D build did not carry the trees (see "Harness defect" below).
- The semantic program is `CompiledProgramContentHashV1.Serialize` of the baked program; the V2 image of
  the generated trees was not measured separately (it is inside the payload's outer preimage).

- Growth is linear: from 100 to 1000 nodes each column grows by a constant per node (last column).
- Per native node the prepared asset carries 353 bytes of payload: 168 in the semantic program (69 in
  the node record, 91 in the debug-map entry, the rest in children and config), about as much again in
  the outer V2 preimage, and the YAML stores it as hex, so 705 bytes per node on disk.
- 48 of the 91 debug-map bytes per node are the source path (`Assets/Size/Trees/size-native-1000.aibt.json`,
  length-prefixed), repeated in every entry; the outer preimage repeats it again.
- A managed tree's `BehaviorTreeSource` carries the source JSON as it is (`jsonCarried` = source
  bytes); the Player compiles it at run time (D9).

#### 8.3 What `NativeGuard.runtime.asset` holds

`raw\prepared-asset-breakdown.txt` (the asset of item 7's fresh project; SHA-256 of the decoded payload
equals `_payloadHash`):

| Part | Bytes | Note |
| --- | --- | --- |
| File on disk | 34,952 | YAML |
| `_payload` on disk | 33,971 characters | the 16,985-byte payload as hex, plus a trailing `y` |
| other fields | 383 characters | `_payloadHash`, `_catalogFingerprint`, `_sourceId`, `_sourceGuid`, `_sourceContentHash`, `_catalogTypeName`, `_subtreeSourceGuids` |
| In the build ("Used Assets", `fresh\logs\build-editor.log`) | 17.0 KB | binary serialization: the payload as bytes |
| **Payload** | **16,985** | `ProductionTreeArchive.Write` |
| magic and version | 8 | |
| semantic program | 7,905 | `CompiledProgramContentHashV1.Serialize`: header 267 (three 64-character hash strings), 37 node records × 69 = 2,557, children 148, slots 234, config 152, defaults 20, **debug map 4,507** (37 entries; the 80-byte source path in each = 3,108) |
| outer V2 preimage | 8,119 | the bytes `NativeCompiledProgramV2Verifier` reads; holds the same 37 source paths again |
| slots, authorities, accesses | 319 + 185 + 433 | binding tables (canonical key, type and default text) |
| scopes, watched, registered types and fields | 16 | empty here |

**Why 3× the program in the build and 6× on disk.** The 5,451 bytes the brief compares with are
Native Guard's compiled V2 byte stream as P11-009 measured it, in the Modules project, where the
source path is shorter (`P11-009 F9\native-guard-bytes-compare.txt`); it was not re-measured here.

- In the build the asset is the payload (16,985 bytes = 3.1×): the program is stored twice, as the
  semantic program (7,905) and as the V2 preimage (8,119), and each copy carries a debug map with the
  full source path per node. The binding tables add 0.9 KB.
- On disk the YAML writes the payload as hex, two characters a byte: 33,971 characters (6.2× the
  program), and the file with its other fields is 34,952 bytes (6.4×).

**What the Player never reads.** Nothing is unread, but one copy is read only to be compared:

- The executed image is built from the semantic program (`NativeProgramImageOwner.cs:186`). The V2
  preimage is hashed on load (`OuterContentHash`) and read by `NativeCompiledProgramV2Verifier.TryValidate`
  (called from `NativeProgramImageOwner.cs:670`), which checks it field by field against the semantic
  program.
- The debug maps' `SourcePath` is read only for diagnostics (`NativeDiagnosticProjectorV1`).
- Not changed here (brief); see the open findings.

#### 8.4 Memory per agent

Probe: `SizeProbe.Memory` in the rebuilt D Player (`build\D-Minimal-r2`, and `D-Minimal-r3` with one more
kind), `-batchmode -nographics`. It loads the Memory scene (the trees are referenced there, so they are
in the build), measures after `GC.Collect` twice, spawns N GameObjects with the component, waits until
every agent has updated at least once (here: 1 frame), waits 30 more frames, and measures again.

- **Managed** = the change of `Profiler.GetMonoUsedSizeLong()` (within 4 KB of `GC.GetTotalMemory` in all 56 measurements).
- **Native** = the change of `Profiler.GetTotalAllocatedMemoryLong()`. It moves in steps of 128 KB, so
  below about 1.3 KB per agent at N = 100 (130 bytes at N = 1000) a figure is not resolved.
- `Profiler.supported` is `False` in a release Player; both counters still report (nonzero, and they move
  with N).
- Every run: all N updated, 0 faulted, exit code 0. Two passes per point; the table gives the mean per
  agent and, where the passes differ, the range. Raw lines: `raw\memory-set-r2.txt`,
  `raw\memory-extra-r3.txt`; the fit: `tools\memory_fit.py` → `raw\memory-fit.txt`. Run folders:
  `run\D-Minimal-r2-memory-*-pass{1,2}\`, `run\D-Minimal-r3-memory-*-pass{1,2}\`.

| What is spawned | Tree nodes | Managed per agent, N = 100 / 300 / 1000 | Native per agent, N = 100 / 300 / 1000 | Native fit: once + per agent |
| --- | --- | --- | --- | --- |
| empty GameObject (baseline) | – | 123 B / – / 70 B | 0 / – / 393 B | – |
| `ProductionTreeHost`, First Agent program, bootstrapped from code | 1 | 4.4 / – / 3.8 KB | 55.1 / – / 51.6 KB | 0.4 MB + 51.2 KB |
| `ProductionTreeHost`, 100-node program | 109 | 145.4 / – / 143.4 KB | 838.2 / – / 838.7 KB | 0 + 838.8 KB |
| `NativeTreeAgent`, First Agent | 1 | 7.2 / 6.7 / 6.3 KB | 760 (630–889) / 322 (308–335) / 132.7 KB | 73.7 MB + 60.1 KB |
| `NativeTreeAgent`, 100-node tree | 109 | 147.3 / 145.1 / 143.6 KB | 1686 / 1224 / 1038 (1029–1046) KB | 74.9 MB + 963 KB |
| `BehaviorTreeAgent`, Simple Agent `guard.aibt` | 17 | 23.3 / – / 17.2 KB | 32.8 / – / 3.9 KB | 3.2 MB + 0.7 KB |

Managed fit, once + per agent: `ProductionTreeHost` 0.06 MB + 3.7 KB (1 node) and 0.2 MB + 143.2 KB
(109 nodes); `NativeTreeAgent` 0.14 MB + 6.1 KB and 0.49 MB + 143.1 KB; `BehaviorTreeAgent` 0.69 MB +
16.5 KB.

**Is the cost per agent the same at 100 and 1000?**

- **Bare hosts: yes.** 51–55 KB native for a 1-node tree, 838 KB for a 109-node tree at both N.
- **`NativeTreeAgent`: no.** The first agents bring a one-time native block of about 74 MB (the
  N = 100 total was 63 MB in one pass and 89 MB in the other), which bare hosts do not have. Past it,
  each agent costs the host's memory plus 9 KB (1 node) or 125 KB (109 nodes) native. Divided by N,
  the block makes 100 agents look 5.7× (First Agent) or 1.6× (100-node tree) as expensive per agent as
  1000.
- **`BehaviorTreeAgent`: almost.** Managed 16.5 KB per agent plus 0.7 MB once; native about 3.2 MB once
  and under 1 KB per agent.

**What grows with the tree.** A host's memory grows with the node count: from 1 to 109 nodes, by
7.3 KB native and 1.3 KB managed per node, per host. 1000 `NativeTreeAgent`s with the 100-node tree
take 1.0 GB native and 144 MB managed. A `BehaviorTreeAgent` with the 17-node guard tree takes about
17 KB (16.5 managed, 0.7 native).

**Harness defect, first attempt.** `SizeAibtDriver.PrepareGrowth` saved the Memory scene with every
template reference as `{fileID: 0}` (set through `SerializedObject`; cause not found). So the first D
build carried none of the generated trees, and all eight memory runs failed ("No runtime tree asset is
assigned", `run\attempt1-null-templates\`). `SizeAibtDriver.FixTemplates` sets the fields directly and
checks the saved YAML (`out\D\templates.txt`, 0 null references); the D Player was rebuilt
(`build\D-Minimal-r2`, `-r3`) and every number above comes from it. The two `hosts` runs of the first
attempt threw a `NullReferenceException` in the probe and did not quit; the harness does not kill a
Player, so they are still running (pids 55472 and 60396, `build\D-Minimal\SizePlayer.exe`).

## Open findings (not fixed here)

- **Authoring tools and the graph's edit session still use the old default** (review follow-up item 5:
  shown, not changed; **resolved** by the "Policy default" part of the review follow-up). Without a policy file,
  the MCP authoring tools (`BuildRegistryAndOptions`) and the Graph window's edit session
  (`BehaviorTreeGraphWindow.CompilerOptions()`) still validate native trees with the Phase 1 default.
  The brief named `validate`, `compile`, `prepare_runtime` and the manifest, and these were outside it.
  - A native tree with Agent or Shared keys can therefore get scope diagnostics while it is edited,
    although the tree list, `validate` and the bake accept it.
  - Not measured in a consumer: the samples' native trees use Tree scope.
- **MCP from `-executeMethod` sees only built-in nodes at first.** `ProjectNodeCatalogCache` (in
  `Editor/Registry/ProjectNodeCatalog.cs`) starts with `ProjectNodeCatalog.Initial()` and discovers
  project nodes on `EditorApplication.delayCall` or `projectChanged`.
  - So a batch-mode script that calls `McpToolDispatcher.Dispatch` right at startup gets AIBT2021 for
    every project node: 8 trees invalid in item 7's first `McpCheck` run.
  - The documented routes (the MCP bridge and the CLI against a running Editor) are not affected: the
    refresh has run before the first request.
  - Older than this task. The fix would change when discovery runs, and it was not made here.
- **Bisect gap.** `46da0de` contains the test that reads the minimal policy from `data-formats.md`, but
  that text arrived in `fee9937`. At `46da0de` alone that one test fails.
- **Console.** The Modules Console buffer holds Editor-internal errors ("Release of invalid GC handle",
  `VirtualArtifacts` loads) and errors logged on purpose by other tests. Two entries came from this
  task's intermediate attempts, both fixed before their commit: CS0103 in `TestAssemblyIsolationTests`,
  and an undeclared `live-target` write in the first cadence test. No error comes from a committed
  state of this task.

- **A prepared asset stores the program twice.** `ProductionTreeArchive.Write`
  (`Runtime/Integration/ProductionTreeArchive.cs:22`) writes the semantic program and the outer V2
  preimage. The Player builds the image from the semantic program (`NativeProgramImageOwner.cs:186`).
  It uses the preimage only to hash it (`OuterContentHash`, `NativeProgramBlackboardContractsV2.cs:278`)
  and in `NativeCompiledProgramV2Verifier.TryValidate` (`NativeCompiledProgramV2Verifier.cs:110`, called
  from `NativeProgramImageOwner.cs:670`), which compares it with the semantic program.
  - Native Guard: 8,119 of 16,985 payload bytes; the 1009-node tree: 174,466 of 356,807 (49%).
  - Dropping it would change the archive format and the verification, so it is a design decision, not
    a fix here.
- **The debug maps repeat the full source path per node.** Each debug-map entry holds the source path,
  in the semantic program and again in the V2 preimage.
  - Native Guard: 37 × 80 bytes of path text = 3,108 of the semantic program's 7,905 bytes.
  - The 1009-node tree: 48,432 bytes of path text in the semantic program's debug map (48 bytes per node
    for a 44-character path), and as much again in the preimage.
  - The path depends on where the tree sits in the project, so a deeper folder makes every node bigger.
- **The YAML stores the payload as hex.** On disk a prepared asset is twice its payload (Native Guard
  33,971 characters for 16,985 bytes). In the build it is binary (17.0 KB). Costs only repository and
  Editor disk space.
- **A native-only Player carries the managed path.** `AIBT.Authoring` (486,400 bytes IL, 12.6 MB C++)
  and `AIBT.Agents` with `BehaviorTreeAgent` and the 60 managed nodes are in B, which uses only
  `NativeTreeAgent`. Under High stripping 230,400 bytes IL and 5.3 MB C++ of `AIBT.Authoring` remain
  (the reference compiler, `NodeRegistry`, `CanonicalTreeJson`), and `AIBT.Agents` keeps 93 types.
  Not traced: why the linker keeps them. Splitting assemblies or adding `link.xml` rules would change the
  package's assembly layout; not done here.
- **A native host's memory grows by about 7.3 KB native and 1.3 KB managed per tree node.** A bare
  `ProductionTreeHost` costs 51 KB native and 3.7 KB managed with the 1-node First Agent tree, 839 KB
  and 143 KB with a 109-node tree, the same at 100 and 1000 hosts. 1000 `NativeTreeAgent`s with the
  109-node tree take 1.0 GB native and 144 MB managed.
  - Each host copies the program's node records, child indices and configuration into its own arrays
    (`SchedulingPolicyDriver.cs:57-65`), so immutable program data is held once per host. The node
    records alone are 69 bytes a node; the rest of the 7.3 KB per node was not attributed.
  - Not a defect by itself, but a cost a user with many agents and large trees needs to know; recorded
    in `performance.md`.
- **`NativeTreeAgent` brings a one-time native block of about 74 MB.** The fit over N = 100, 300 and
  1000 gives 73.7 MB (First Agent) and 74.9 MB (100-node tree) allocated once; the N = 100 total was
  63 MB in one pass and 89 MB in the other. Bare hosts bootstrapped from code do not have it. Past it
  the agent adds 9 KB (1 node) or 125 KB (109 nodes) native per agent over the host.
  - Candidate places, not traced: the world and scheduler (`NativeTreeAgent.StartHost` →
    `ProductionTreeWorld.TryRegister`, `ProductionTreeScheduler`, the jobs drivers
    `ProductionAgentFrameDriverV1` and `ProductionPipelinedGroupDriverV1`), or Unity's job-system
    allocators on first use.

## Review follow-up (2026-11-24)

Brief: `C:\UnityProjects\_aibt_acceptance\P11-014\brief-review-fixes.md`; review:
`C:\UnityProjects\_aibt_acceptance\P11-015\acceptance\independent-review-824a378-87ef22a.md`. Items 1-3 and the
P11-014 half of item 4 are in the P11-014 evidence. Raw: `C:\UnityProjects\_aibt_acceptance\P11-015\raw\review-followup\`.

| Commit | Item | Content |
| --- | --- | --- |
| `d433a1a` | 4 | `TestAssemblyIsolationTests` and `PipelinedJobsCadenceTests` (with the P11-014 tests) |
| `5534826` | 5 | default policy after a failed subtree expansion; `prepare_runtime` and edit-tool wording |
| `9901ebf` | 6 | shipped docs and `Tools~` no longer point into excluded folders |

**4. Weak tests (this task's half).** `TestAssemblyIsolationTests` passed with a wrong root (no asmdef found);
it now asserts at least five assembly definitions and the names of the three F14 fixtures plus
`AIBT.Editor.Tests` and `AIBT.Integration.Tests` (26 found). 1/1. `PipelinedJobsCadenceTests`: the
`shared-loop16` case resolves its tree through the package path helper instead of a fixed path and stays
Inconclusive without `Benchmarks~` (logged: 18 updates in 120 frames, intervals 3, 26 or 44, mean 6.56). 5/5.

**5. One default policy for every tool (D1 tail).**
- The disagreement exists. A temporary test (not committed) wrote a native tree with an Agent-scope key, no
  policy file: `validate` returned `valid: true`; `add_node` with `dryRun` on the same tree was refused with
  AIBT2030 (raw `item5-policy-probes.txt`).
- **Stop (brief condition), not routed.** Routing the authoring tools through `ProjectNodeCatalog.DefaultPolicy`
  inverts `McpAuthoringToolDispatcherTests.CreateTreeWithAgentScopeKeyRequiresPolicyOptInAndWritesV2` ("Without a
  project policy opting in, Agent scope must still be rejected exactly as before"), which the ADR-P9-001
  amendment keeps ("`simulate` and the MCP authoring tools without the file still use it for a native tree").
  The graph session (`BehaviorTreeGraphWindow.CompilerOptions()`) is a static `Phase1` that never reads the
  file, with or without one; no reason for that is recorded. Neither was changed, so the authoring tools still
  ignore an unreadable file without AIBT9008. `ai-and-mcp.md` now says what each tool does.
- **Failed expansion (fixed).** Without a file, `validate`/`compile` chose the default on the unexpanded parent
  when the expansion failed. A parent whose managed nodes all sit in a subtree, with a second reference to a
  missing tree, got AIBT2039 three times and AIBT2040 for the subtree's managed nodes beside the real AIBT3020.
  `ProjectNodeCatalog.DefaultPolicy(document, resolver, sourceId)` now decides on the parent and every subtree
  that resolves when the expansion fails. Test
  `McpSubtreeToolsTests.WithoutAPolicyFile_AFailedExpansion_ReportsItsCause_NotTheNativeDefaultRefusingTheSubtreesManagedNodes`:
  failed before (AIBT2039, AIBT2040, AIBT3020), passes after (AIBT3020 only). The tree list is not affected: it
  stops at a failed expansion before choosing a policy.
- **`prepare_runtime`.** It applies the policy only through its `validate` gate; the bake compiles every native
  tree with `Phase1` (`ProductionTreeAssetBaker.cs:60`), and the header records `Phase1`'s policy hash. Without
  a file the outcome cannot differ: `NativeDefaultPolicy` differs from `Phase1` only in the scope flags, and the
  generated compiler validates with both scope flags on (`GeneratedCompiledProgramV2.cs:353-357`). With a file
  that relaxes a rule `Phase1` keeps, `validate` accepts and the bake refuses: a temporary test with
  `"unreachableNodes": "warning"` and an orphan node got `valid: true` with warning AIBT2020, then
  `prepare_runtime` `success: false` with AIBT2020 as an error. A native node declared `Deterministic = false`
  under `"requireDeterministicNodes": false` is the same case by code reading. So no asset is ever built that
  `validate` accepted under different outcomes; a refusal can follow an acceptance. Not changed (it would change
  every baked asset's policy hash); documented in `ai-and-mcp.md` and `ProjectNodeCatalog.cs`.
- Tests: McpSubtreeTools 30/30, McpVerificationToolDispatcher 33/33, McpAuthoringToolDispatcher 20/20.

**6. Shipped docs and `Tools~` (finding: links into excluded folders).**
- Links in `README.md`, `scheduling-benchmark-report.md`, `compatibility-matrix.md` and ADR-P7-011 now give the
  repository path as text and say it is in the AIBT repository only; `performance.md`, `compatibility-matrix.md`,
  `hot-reload.md`, `roadmap.md` and `scheduling-benchmark-report.md` carry one such note per page,
  `ai-and-mcp.md` and the generated `recipes.md` (and its generator) one per sentence, `decisions.md` one for the
  ADRs. The numbers of "Size and memory" in `performance.md` are untouched. The report's chart is copied to
  `Documentation~/images/jobs-vs-non-jobs.svg`; the original stays.
- What the consumer docs tell a consumer to run: only `Tools~/aibt.ps1` (`cli.md:17`). It ran from the
  extracted archive: it built `MCP~/Cli` and printed the usage, exit 0 (raw `item6-aibt-ps1-from-archive.txt`).
  `development-commands.md` addresses the repository and now says which scripts are repository-only.
- Repository-only scripts get `export-ignore` and `.npmignore` lines: `Verify-Static.ps1`, `Verify-Schemas.ps1`
  (read `Planning~`), `Tools~/Verification/P2/` (reads `Benchmarks~`). The CI workflows that call
  `Verify-Static.ps1` run in the repository. The folders themselves are now excluded too: `git archive` had kept
  `Planning~/` and `Benchmarks~/` as empty directory entries.
- Proof, `git archive HEAD` at `9901ebf`: 2 419 entries, none under the excluded folders, none of the three
  scripts (raw `archive-9901ebf-listing.txt`). Grep for the three names: 206 hits in 80 files, **0 Markdown
  links**; every hit is classed in `archive-9901ebf-grep-explained.txt`: the exclusion rules (9); shipped docs
  with the repository-only note (39); ADRs, covered by the note in `decisions.md` (83); the protected normative
  specification `agent-shared-blackboard-v1.md` (2, unchanged); CHANGELOG history (19); `AGENTS.md` and the
  `.github` workflows, which run in the repository (5); the Swarm Arena README's benchmark command (1, left to
  the other session); `PipelinedJobsCadenceTests`, the one code path that reads an excluded folder and is
  Inconclusive without it (3); comments in shipped `Tools~` scripts (3) and in code (42).
- `Verify-Static.ps1` passes: 195 work items, 7 schemas.

Not verified: an `npm pack` (only `git archive`); a consumer install of the new tarball (the archive was
extracted and `aibt.ps1` run, no Unity project imported it); the authoring tools and graph session with an
Agent-scope native tree in a consumer project.

### Policy default (decision after item 5's stop)

Brief: `C:\UnityProjects\_aibt_acceptance\P11-014\brief-policy-default.md`; independent read:
`C:\UnityProjects\_aibt_acceptance\P11-015\acceptance\policy-default-read.md` (confirmed where tested; one
correction below).

| Commit | Content |
| --- | --- |
| `fe43768` | ADR-P9-001, second amendment (the authoring tools leave `Phase1`; `simulate` keeps it) |
| `b7378b3` | the authoring tools and the graph edit session use the shared default; neighbours (a) and (b); the `prepare_runtime` note |
| `dcb1b2d` | `ai-and-mcp.md`, `data-formats.md`, `known-limitations.md`, CHANGELOG, the Burst wording |

What changes for a user without `.aibt/policy.json`:
- `create_tree`, `add_node`, `set_blackboard_keys` and the other edit tools accept a native tree with Agent
  or Shared keys, as `validate` does (before: AIBT2030, nothing written). A tree with a managed node and such a
  key is still refused.
- The Graph window's edit session no longer shows AIBT2030 for a tree its list calls Valid
  (`BehaviorTreeGraphWindow.CompilerOptions()` is `NativeDefaultPolicy`; `ValidateAuthoring` switches managed
  trees as before).
- An unreadable file: the authoring tools report warning AIBT9008 (through `ReadProjectPolicy`) instead of
  ignoring it, and use the default.
- With a readable file: unchanged for native trees; the file decides for managed trees too (neighbour a).

Neighbours, reproduced before deciding:
- (a) With `allowManagedNodes: false`, `add_node` of `aibt.unity.wait-seconds` (dry run) was **accepted**
  (`ValidateAuthoring` replaced any options with the managed policy). Fixed: the switch happens only from a
  built-in default (`ProjectNodeCatalog.IsBuiltInDefault`); now refused (AIBT2040 under that file).
- (b) Correction to the read: the bypass needs a subtree node without a reference handler. My first test used
  `aibt.stdlib.wait`, which has one, so the reference compiler checked the scope and refused (the test passed
  before). With `aibt.native.fail` (no reference handler) it reproduced: under a file with `supportsAgentScope: false`, `add_node` on the tree without a reference was
  refused (AIBT2030) and on the same tree with a subtree reference **accepted** (the native compiler expands with
  both scope flags on). Fixed: the resolver overload makes `validate`'s check (expansion under the policy, then
  the expanded tree) before compiling.

AIBT2030's text ("recognized but unsupported by the selected execution capabilities") names no opt-in, so it is
true everywhere it can now appear: a file with the flag false, a managed tree, `simulate` and the previews.
Not changed.

Tests (failed before the fix unless noted):
- `McpAuthoringToolDispatcherTests`: `CreateTreeWithAgentScopeKey_UsesTheEditorDefaultWithoutAPolicyFile_AndThePolicyWithOne`
  (rewritten from `CreateTreeWithAgentScopeKeyRequiresPolicyOptInAndWritesV2`, the only inverted expectation),
  `EditToolsOnANativeAgentScopeTree_AreAcceptedWithoutAPolicyFile_AndRefusedByAFileWithTheFlagFalse`,
  `AnUnreadablePolicyFile_IsReportedAsAIBT9008_ByCreateTreeAndTheEditTools`,
  `WithAFileThatForbidsManagedNodes_AddingAManagedNode_IsRefused_AsValidateRefusesTheTree`,
  `AnAgentScopeTreeWithASubtreeReference_IsCheckedLikeTheSameTreeWithoutOne`,
  `CreateTree_OfAManagedTreeWithAnAgentScopeKey_IsStillRefusedWithoutAPolicyFile` (passed before: a pin). 25/25.
- `GraphSessionDefaultPolicyTests.ANativeAgentScopeTree_TheListCallsValid_HasNoAIBT2030InTheEditSession` 1/1.
- `McpVerificationToolDispatcherTests.PrepareRuntime_OfATreeAPolicyFileRelaxes_IsRefusedByTheBake_AndSaysWhy`
  (the refusal's new `note`) 34/34.
- Unchanged and green: McpSubtreeTools 30/30, AgentConsumerTools 14/14, McpSimulationTools 4/4,
  ManagedObserverRefusal 3/3, NativeObserverWarning 10/10, TreeScopeSlotNeverWrittenNativeBinding 19/19,
  SubtreeGraphEditor 13/13, GraphEditSession 6/6, SemanticEditTransaction 3/3, SemanticPatchTransaction 4/4,
  BehaviorTreeGraphAdapter 15/15, GraphConsumerWorkflow 4/4, GraphEditorWorkflow 15/15,
  GraphNativeLiveBlackboard 15/15, McpAuthoringJson 2/2, McpCustomToolsToolDispatcher 7/7,
  McpDocumentationGenerators 14/14.

Documented limitations (`known-limitations.md`): the Graph window does not read the file; the bake compiles
with the built-in options; `simulate` and the previews run Tree scope only.

Not verified: the Graph window opened on such a tree (the session's check is tested through
`ValidateAuthoring` with the window's options, not through the window); the CLI against a running Editor; a
consumer project.

## Deviations

- **A README fix after the planning commit.** The README step was wrong (item 7) and was fixed in
  `baa579e`, after the planning commit `3a1f061`. The tarball comes from `baa579e`.

- **Response schema without a version bump.** The manifest response schema changed with no version bump
  (reason under item 1).
- **D7 range.** D7's open range is "before 6000.6", not "before 6000.5" (reason under item 4).
- **Performance documentation after the tarball.** Item 8's table in `Documentation~/performance.md`
  was added after the tarball of item 7 (`baa579e`), so that tarball does not contain it. Nothing else
  in the package changed after `baa579e`.
- **Harness catalog refresh.** The item 7 harness calls the Editor's node-catalog refresh by reflection
  before its MCP checks (open finding "MCP from `-executeMethod` …"); without it the checks fail.
- **Two Size Players left running.** The first memory attempt's two `hosts` runs hung after a
  `NullReferenceException` in the probe (the template references were null, see 8.4). The harness never
  kills a process, and this task's rule is to kill nothing, so they are still running: pids 55472 and
  60396, `C:\UnityProjects\_aibt_f14\size\build\D-Minimal\SizePlayer.exe`, about 100 MB each. They can be
  closed by hand. The D Player was rebuilt into new folders instead.
- **An extra probe kind.** Item 8.4 adds `empty` (baseline) and `hosts100` (bare hosts with the 100-node
  program) to the brief's kinds, and a third point (N = 300) for `NativeTreeAgent`, to place the costs.
