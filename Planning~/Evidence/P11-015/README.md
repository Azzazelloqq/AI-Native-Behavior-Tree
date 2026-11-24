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
| planning commit | 6 | task cards, work items, MASTER_PLAN, P11-011 evidence, this evidence up to item 6 |
| last commit | 7, 8 | the fresh-install proof and the size measurements |

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

## Open findings (not fixed here)

- **Authoring tools and the graph's edit session still use the old default.** Without a policy file,
  the MCP authoring tools (`BuildRegistryAndOptions`) and the Graph window's edit session
  (`BehaviorTreeGraphWindow.CompilerOptions()`) still validate native trees with the Phase 1 default.
  The brief named `validate`, `compile`, `prepare_runtime` and the manifest, and these were outside it.
  - A native tree with Agent or Shared keys can therefore get scope diagnostics while it is edited,
    although the tree list, `validate` and the bake accept it.
  - Not measured in a consumer: the samples' native trees use Tree scope.
- **Bisect gap.** `46da0de` contains the test that reads the minimal policy from `data-formats.md`, but
  that text arrived in `fee9937`. At `46da0de` alone that one test fails.
- **Console.** The Modules Console buffer holds Editor-internal errors ("Release of invalid GC handle",
  `VirtualArtifacts` loads) and errors logged on purpose by other tests. Two entries came from this
  task's intermediate attempts, both fixed before their commit: CS0103 in `TestAssemblyIsolationTests`,
  and an undeclared `live-target` write in the first cadence test. No error comes from a committed
  state of this task.

## Deviations

- **Response schema without a version bump.** The manifest response schema changed with no version bump
  (reason under item 1).
- **D7 range.** D7's open range is "before 6000.6", not "before 6000.5" (reason under item 4).
