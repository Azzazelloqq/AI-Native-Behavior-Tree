# Changelog

All notable changes to this project will be documented here. The project follows Semantic Versioning while public APIs remain explicitly marked as experimental before `1.0.0`.

## [Unreleased]

### Subtrees, stage 4: MCP, CLI, user guide (P11-001)

- **MCP tools for subtree references.** `add_node` (and an `add` operation of `apply_domain_patch`)
  accepts a node `{id?, subtree: {treeId, interfaceVersion?, keys?}}`; interface keys it does not
  list are connected to same-named parent keys (optional `connectSameNamedKeys`). New tools
  `set_subtree_keys`, `extract_to_subtree` (new tree file and reference in one transaction; no file
  is left behind on failure) and `unpack_subtree` (SemanticEdit), and `find_subtree_usages` (Read).
  `inspect_tree` takes an optional `expanded` and then adds the expansion the compiler sees;
  `get_project_manifest` lists each tree's `interface` and `referencedTreeIds`. MCP server tools
  `aibt_set_subtree_keys`, `aibt_extract_to_subtree`, `aibt_unpack_subtree`,
  `aibt_find_subtree_usages`.
- **References resolve in MCP.** `validate`, `compile` and `prepare_runtime` resolve references
  through the project's trees; a missing, duplicate or recursive subtree is a diagnostic
  (`AIBT3020`–`AIBT3022`). An MCP edit of a tree with references is accepted only if the tree still
  expands and compiles (before, every such edit was rejected with `AIBT3020`). Trees without
  references give the same responses as before.
- **CLI:** `tree expand`, `tree usages`, `tree extract`, `tree unpack`, `tree keys`.
- **Naming policy and expanded keys (fix).** The project `blackboardNaming` policy now checks the
  part of a blackboard key name after the last `:`. Before, an expanded private key `r:key` always
  failed `AIBT2038` under `snake_case`, `camelCase` or `PascalCase`, so MCP `validate`, `compile`
  and edits rejected every tree whose subtree has a private key or a constant binding. Names without
  `:` are checked as before; the policy hash and the policy file format are unchanged.
- **Graph editor:** the running highlight of a reference card is a thicker ring set apart from the
  card's teal border, so it is visible (style sheet only).
- **Documentation:** new [Subtrees](Documentation~/subtrees.md) guide; `cli.md`, `ai-and-mcp.md` and
  the generated workflow guide describe the new tools.
- **Unchanged:** existing tool names, argument shapes, results and permission categories (only
  optional arguments and result fields added), `extract_subtree`/`inline_subtree`, public API,
  executors, ABI v2, compiled program and trace formats, specifications, package version.

### Subtrees, stage 3: graph editor (P11-001)

- **Subtrees in the graph editor.** The palette lists "Subtree…" first and opens a picker over the
  project's trees. A reference is one card with a SUBTREE badge and one row per interface key; the
  inspector edits its key map (parent key, constant or not connected), creates a missing parent key
  and connects same-named keys. Problems of the expansion appear on the reference card.
- **Open Subtree** opens the subtree in its own window with a breadcrumb back to the parent (view
  state only; the layout format is unchanged). **Extract to Subtree…** moves a node and its
  descendants into a new tree file (written on the next Save) and leaves a reference; **Unpack
  Subtree** replaces a reference by its expansion. All of them are single undo steps.
- **Live debugging and trace.** A running subtree lights its reference card in the parent; a
  subtree window opened from a reference follows that instance only and shows its live blackboard
  values. The trace timeline labels subtree nodes "reference / node".
- **Edits keep the subtree interface.** Graph and MCP authoring edits of a format-3 tree no longer
  drop its `interface`, and copying a reference keeps its `subtree`.
- **Stage-2 follow-ups.** A tree with subtrees whose only problem is node types the project catalog
  does not know imports as `Needs registry` (was `Errors`). The tree-ID index also covers trees in
  packages (`Packages/<name>/...`).
- **Unchanged:** public API (only internal additions), executors, ABI v2, compiled program and
  trace formats, layout format, `CodeGen~`, `MCP/**`, package version. MCP/CLI support is stage 4.

### Subtrees, stage 2: native path, import, baking (P11-001)

- **Generated bindings may name expanded keys (owner decision 9).** A node's `bindings` value now
  uses the blackboard key ID grammar, so a native node inside a subtree can bind to an instance's
  private key `r:key` or to an interface key given a constant. Every value accepted before is still
  accepted; binding IDs keep their grammar. The expander no longer reports `AIBT3026` for these
  bindings. `tree.schema.json` and `agent-shared-blackboard-v1.md` say the same.
- **Native compilation with subtrees.** New overloads
  `GeneratedCompiledProgramV2Compiler.Compile(..., ISubtreeResolver)` and
  `Compile(SubtreeExpansion, ...)`, plus `SubtreeExpander.Expand(..., RegisteredBlackboardTypeCatalog)`.
  The overload without a resolver reports `AIBT3020` for a tree with references (it reported
  unrelated validation errors before). Trees without subtrees compile to the same bytes.
- **Import.** `BehaviorTreeImporter` is version 3 (every `.aibt` file re-imports once). A tree with
  references is stored as its expanded document with per-node subtree sources, depends on every
  subtree file and on a tree-ID index (`AIBT/Subtrees/TreeId/<id>`), and is re-imported when a
  subtree file changes or a tree ID gains or loses a file (`AIBT3021` while two files share one ID).
- **Baking.** `ProductionTreeAssetBaker.Create(..., ISubtreeResolver, out ...)` is new; the existing
  overload resolves subtrees against the project's trees. `ProductionTreeAsset` records the subtree
  files it was prepared from (internal serialized field), its source hash covers them, and editing a
  subtree file marks dependent assets stale and re-prepares them.
- **Unchanged:** executors, ABI v2 (`ExpectedPublicAbiV2.txt`), compiled program format and version,
  trace format, node registry, `CodeGen~`, package version, `ProductionTreeHost`, `NativeTreeAgent`,
  `ProductionTreeScheduler`. The graph editor and MCP/CLI are stages 3–4.

### Subtrees, stage 1: format, validator, expander, managed path (P11-001)

- **Tree format version 3 (additive).** A `.aibt.json` file with `formatVersion: 3` may declare a
  top-level `interface` (`version`, `keys`) and reference nodes (`subtree`: `treeId`,
  `interfaceVersion`, `keys`). Version 1 and 2 files are read and written exactly as before.
  `TreeDocument.LatestFormatVersion` stays 2; the new constant is `TreeDocument.SubtreeFormatVersion`.
- **Expansion before compilation.** `SubtreeExpander.Expand` replaces each reference `r` by a copy
  of the subtree whose nodes and private keys are renamed `r:<id>`; interface keys become the bound
  parent key or a private key holding the constant. The result is an ordinary format-2 document
  (`AIBT3020`-`AIBT3028` on failure). Subtrees are found through `ISubtreeResolver`
  (`DirectorySubtreeResolver`, `InMemorySubtreeResolver`).
- **Additive overloads.** `ReferenceCompiler.Compile(..., ISubtreeResolver)` and
  `Compile(SubtreeExpansion, ...)`; `BehaviorTreeSource.FromJson(json, name, resolver)` and an
  8-argument `SetContent` that keeps the per-node subtree sources; internal `ManagedTreeProgram`
  overloads. Debug-map source paths of inner nodes name the subtree file.
- **Validator.** `AIBT2047`-`AIBT2050` (reference shape, key target, root reference, interface).
- **Behaviour cases** resolve subtrees relative to the case tree's directory.
- **Unchanged:** every existing tree compiles to byte-identical hashes (409 + 57 checked lines);
  executors, ABI v2, compiled program format, trace format, node registry. The importer, native
  path, graph editor and MCP/CLI are stages 2–4 and not part of this change.
- Specifications updated per ADR §4.8 (`canonical-json-v1`, `identity-and-hashing-v1`,
  `compiled-program-v1`, `blackboard-v1`, `execution-semantics-v1`, `diagnostics-v1`,
  `editor-layout-v1`, `data-formats`).

### Subtrees: decision accepted (ADR-P11-001, AIBT-049)

- A tree will be usable as a node of other trees through a
  reference node that is replaced, before compilation, by a renamed copy of the referenced tree
  (tree format version 3, additive). Executors, ABI v2, the compiled program format and the trace
  format do not change. See `Documentation~/decisions/ADR-P11-001-subtrees.md`.

### Specification: first update after a structural hot-reload reset

- `execution-semantics-v1.md` now states that in the first update after a structural edit that
  resets a composite's cursor, the reset replaces update-start processing, so no retaining
  composite on the active path has a guard pass in that update. Both executors already behave this
  way (P10-014 finding R2); no code changed.

### Native fixes from the P10-013 code review (P10-014)

- **Abort through a Parallel (D1, native, pre-existing).** Suppose an abort whose target lies above
  a Parallel with suspended branches: a `Timeout` at its deadline, a v1 reactive reset, an observer
  reset, a hot-reload reset or an outer Parallel's terminal decision. It now continues to its own
  target after the Parallel has aborted its branches, as in the reference executor. Before, the
  Parallel took over the target:
  - `timeout [parallel [a, b]]` returned `Success` and left a branch's frames behind;
  - a v1 reactive composite over a Parallel faulted the agent (`NativeLifetimeStateInvalid`);
  - a nested Parallel's terminal decision aborted the outer Parallel instead of returning.

  A tree stop that replaces such an abort still runs to the root.
- **Parallel branch restore (D2, native, pre-existing, = P10-013 finding F-S2-1).** Restoring a
  suspended branch while fewer frames were free than the branch is long overwrote a lower branch.
  In small v1 trees such as `parallel [memory-sequence [ok, work], other]`, one leaf was ticked
  twice and another never ticked. Every program now uses the in-place rotation that programs with
  retaining composites already used. Where the old copy was correct, the frames come out the same.
- **Retaining composites (D3).** A tree stop or observer reset in the one step after a Parallel
  guard's takeover no longer leaves the rest of that visit pending into the next update. Before, the
  next update of a reused machine faulted.
- **Hot reload.** `NativeHotReloadFullRestart.TryRestart` restarts a v1 reactive composite with a
  running branch (P10-013 finding F-S2-2). `NativeHotReloadStateMigration.TryMigrate` refuses while a
  retaining composite's guard pass or takeover is in progress (parked frames it cannot carry);
  migration in an open update without one is allowed as before.
- No public API, trace format, compiled format or reference-executor change. Details and the
  remaining open questions are in `Planning~/Evidence/P10-014/README.md`.

### Reactive composites that keep their running child, stage 3: IL2CPP measurement (ADR-P10-013)

- **Benchmark only, no package code changed.** New Player-harness trees `io-retain16` and
  `act-retain16` (the v1 reactive trees with the retaining root) and sets `sim-retain`, `sim-v1` and
  `sim-retain-thinks` in `Benchmarks~/Phase10/PlayerPerf`. `runs.jsonl` now reports cancels per
  reason, live actions (consumed completions) and, on request, thinks per tick.
- **Retaining root, measured (500 agents, `Spread(5)`):**
  - An action is never preempted; every completion is consumed and gives an order (166.6 per tick;
    v1: 0). `Step` allocates 0 bytes per tick.
  - `io-retain16` is not slower than `io-reactive16`: 0.99× with 23 job workers, 0.93× with 0.
  - `act-retain16` costs about 2× `io-retain16`, so the ≤ 1.2× target is missed. Completions now
    wake the agent and restart work, which gives 2.33× the thinks; per think it costs the same or
    less.
- **v1 trees on the P10-013 package:** hosts and reactive trees are unchanged. The cheapest loop
  trees are about 8 % slower, with 23 job workers only (reported, not investigated). Details in
  `Planning~/Evidence/P10-013/README.md`, stage 3.

### Reactive composites that keep their running child, stage 2: native executor (ADR-P10-013)

- **Native support:** `aibt.core.reactive-sequence-retaining` and `aibt.core.reactive-selector-retaining`
  now run on the native lifecycle machine: production tree assets (`NativeTreeAgent`,
  `ProductionTreeHost`), graph execution, generated dispatch and the simulation container
  (`TreeSimulation`), in place and inside the Burst agent-frame job, with no managed allocation per
  update. The stage-1 rejection is gone: the generated compiler no longer reports `AIBT3012` for them,
  and native machines and generated runtime definitions build from programs that contain them.
- **Same behaviour as the reference executor,** record for record: guard pass, resume of the kept
  branch, takeover (the kept branch is aborted with `Explicit` after the guard's records), aborts from
  above during a guard pass or a takeover, update-start processing and step counts, under Immediate,
  step budgets of one step, BatchedJobsSameFrame and JobsAgentFrame. The five `retaining-*` golden
  behaviour cases pass on the native adapter.
- **Simulation container:** an action under a retaining composite is started once, reads its
  completion and is preempted with one `Cancelled` (`Preempted`); the "not under a reactive
  composite" guidance in `simulation-container.md` now points to the retaining composites.
- **Hot reload:** a retaining composite is a composite for the cursor-reset rule; changing a node
  between v1 and retaining is `Incompatible` and restarts its subtree.
- **Unchanged:** the native behaviour of every existing node kind (callbacks, order, step counts),
  ABI v2 (`ExpectedPublicAbiV2.txt`), the compiled format version, the trace format, the package
  version, `ProductionTreeHost`, `NativeTreeAgent` and `ProductionTreeScheduler`. No public API change.

### Reactive composites that keep their running child, stage 1 (P10-013 finding F2, ADR-P10-013)

- **New built-in composites:** `aibt.core.reactive-sequence-retaining` ("Reactive sequence (keeps
  running child)") and `aibt.core.reactive-selector-retaining` ("Reactive selector (keeps running
  child)"). On every update after the one they are entered in, they re-evaluate the children before
  the running one as a *guard pass* and then resume the running child instead of aborting and
  re-entering it. Long work under them makes progress: `aibt.stdlib.wait` with `ticks >= 2` finishes,
  a `Timeout` keeps its deadline, an `on-enter` `Cooldown` is entered once. A guard that ends the
  composite or starts running aborts the kept branch with reason `Explicit`, after the guard's own
  records. Every child before the running one is a guard, so put multi-step work in one child
  (`retaining-seq [cond, memory-seq [stepA, stepB]]`).
- **Reference executor only in this stage.** Managed agents (`BehaviorTreeAgent`, `ManagedTreeInstance`)
  run them. The native path rejects them explicitly until stage 2: the generated compiler (production
  tree assets, graph execution, native-only validation) reports `AIBT3012` at `/nodes/<id>/type`, and
  building a native machine or a generated runtime definition from a program containing them fails.
- **Validation:** an abort observer whose parent is a retaining composite is an error
  (`InvalidObserverContext`, "Retaining reactive composites do not support observers yet").
- **Unchanged:** v1 `reactive-sequence`/`reactive-selector` (manifests, behaviour, traces; new pin
  tests), ABI v2, the compiled format version, the trace format, the package version. Adding the two
  manifests changes the built-in registry hash and therefore compiled content hashes; fixtures that
  pin them were regenerated. `aibt.core.` is now documented as reserved for structural composites and
  decorators executed by the native lifecycle machine (amended P7-028 note); adding one needs an ADR.
- **Public API (additive):** `BuiltInNodeManifests.RetainingReactiveSequenceTypeId` and
  `RetainingReactiveSelectorTypeId` (`AIBT.Authoring`).

### Simulation container: default step limit (P10-011 finding F1)

- **Changed (experimental API):** `TreeSimulationSettings.Default` now sets `MaximumStepsPerThink` to
  10 000 instead of the host's 1 000 000, so a runaway tree faults its agent
  (`NativeUpdateStepLimitExceeded`) quickly instead of holding `Step`. Set the field explicitly for
  trees that need more steps in one think. Hosts are unchanged.

### Simulation container, stage G4 (P10-012, experimental)

- **New, experimental until stage G5:** debugging for `AIBT.Simulation`.
  - **Traces.** `TreeGroup.TrySetTraced(agent, bool)` traces selected agents: why each think
    happened (`ThinkReasons`), the input rows it read, the package's native lifecycle records, commands,
    action starts, cancels and consumed completions, faults and teardowns, read with `TryCopyTrace`
    into a `NativeList<AgentTraceRecord>`. Bounded by `AgentTraceSettings` (`TryConfigureTracing`:
    agents per group, a ring of records per agent that drops the oldest and counts, records per
    think). New result `NotTraced`.
  - **Active path.** `TryGetActivePath` (`NativeList` or `Span` of `ActivePathNode`) and
    `AgentDebugState` for any agent between steps, `TryCopyLiveActions`, `TryGetAgentAt`, and
    `DescribeAgent` for readable text.
  - **Registry.** `TreeSimulationDebug.Register/Unregister/CopyRegistered`: an opt-in, weak list of
    containers for tools, Editor and development builds only. `TreeSimulation.CopyGroups` lists a
    container's groups.
  - **Tools.** Read-only MCP tools `runtime_simulations`, `describe_agent`, `read_agent_trace` and
    CLI `runtime simulations`, `agent describe|trace`.
- Tracing changes no result on any path or worker count, and `Step` still allocates nothing while
  tracing. Untraced agents pay one test per think.
- Hosts: unchanged. A per-slot "has snapshot bindings" flag that skipped input staging was measured
  (IL2CPP host-check, ABBA against G3: 0.99–1.02) and recovered nothing measurable, so it was reverted.
  The cost of hosts relative to G1 comes from elsewhere. ABI v2, the compiled format and the generator
  are unchanged.

### Simulation container, stage G3 (P10-011, experimental)

- **New, experimental until stage G5:** external long-running actions in `AIBT.Simulation`. Burst
  nodes start actions through `AsyncOperationHandle<TStart, TCancel>` and read the game's completion
  through `CompletionHandle<T>`, in place and in agent-frame jobs alike. After each `Step`,
  `TreeGroup.GetActionEvents()` lists the step's `Started` and `Cancelled` events (`ActionEvent`:
  kind, `ActionHandle`, `ActionCancelReason`, `FaultCancel`) in slot order, then the order they
  happened; read their values with `TryGetStartPayload<T>`/`TryGetCancelPayload<T>`. Between steps
  the game ends an action with `TryComplete(handle, ActionOutcome)` or
  `TryComplete<T>(handle, outcome, value)`, which wakes the agent for the next step. Every started
  action ends exactly once: its completion is consumed, or it gets one `Cancelled` event (preempted,
  timeout, node ended, fault, agent removed or restarted, group removed). A completion that meets a
  preemption in the same step loses. Under a reactive composite (which aborts its running child at
  every update) an action is cancelled and restarted at every think and is never consumed; put long
  actions under memory composites. `TreeGroupSettings.ActionCapacityPerAgent` (default 2) bounds
  live actions and starts per think; overflow faults the agent with `CapacityExceeded`, never throws.
  New results `StaleAction` and `AlreadyCompleted`; `Unsupported` is no longer returned. Catalogs
  generated before G2 are refused for programs with actions (`CatalogTooOld`). See
  `Documentation~/simulation-container.md`.
- Hosts (`ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`) are unchanged: they
  still refuse programs with external actions at bootstrap (pinned by a test). ABI v2, the compiled
  format and the generator are unchanged.

### Simulation container, stage G2 (P10-010, experimental)

- **New, experimental until stage G5:** game inputs and commands for `AIBT.Simulation`.
  `TreeGroup.TryBindInput(InputChannel<T>, NativeArray<T>)` binds a game array indexed by agent
  slot; Burst nodes read their agent's row through `SnapshotReadHandle<T>` (new short form
  `[AibtInput("id")]`). `Step` checks every bound array (unbound, disposed or shorter than the
  group's capacity → `InputMissing`/`InvalidArgument`, nothing runs) and copies the rows it needs
  once per step, so the game may write its arrays again as soon as `Step` returns. Nodes emit
  through `CommandHandle<T>`; `TryGetCommands(CommandChannel<T>, out …)` / `GetCommands` return
  them in slot order, then emission order, until the next step. `TreeGroupSettings.CommandQuotaPerAgent`
  (default 4 per channel per think) bounds them; overflow faults the agent with
  `AgentFault.Cause = CapacityExceeded`, never throws. `TrySetWakeOnChange(channel, true)` (off by
  default) wakes an agent whose input row changed (byte compare of the canonical row). New results
  `InputMissing`, `TypeMismatch`, `CatalogTooOld`, `Unsupported`, `UnknownChannel`. Programs with
  external actions (`AsyncOperationHandle`, `CompletionHandle`) are refused with `Unsupported`
  (stage G3). See `Documentation~/simulation-container.md`.
- Generator (additive): catalogs now also emit a binding-id table
  (`IGeneratedBindingIdentityV1`). An older catalog still runs in the container in place with its
  commands discarded; binding inputs to it returns `CatalogTooOld`. ABI v2 is unchanged.
- Hosts (`ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`) are unchanged: they
  still refuse snapshot bindings and registered command payloads as before (pinned by a test).
- Player builds: the link.xml AIBT adds to a stripped Player now also keeps every
  `[AibtBurstValue]` type and the `AibtBurstValue`/`AibtValueField` attributes. Without them an
  IL2CPP Player with High stripping could not bind a registered input or command type
  (`TypeMismatch`).
- Measured (IL2CPP Player, 500 agents, `Spread(5)`, all input rows written every tick, ABBA against
  the G1 trees, 23 job workers):
  - `TreeSimulation.Step` median goes from 52.1 to 53.0 µs for `loop16` (5.5 commands per tick).
  - It goes from 195.8 to 205.2 µs for `reactive16` (100 input reads and 100 commands per tick).
  - Step allocates 0 bytes per tick.
  - Details are in `Planning~/Evidence/P10-010/README.md`.

### Simulation container, stage G1 (P10-009, experimental)

- **New, experimental until stage G5:** `AIBT.Simulation.TreeSimulation`, a GameObject-free world
  the game advances with `Step(tick)`. It holds `TreeGroup`s of agents that run one
  `ProductionTreeProgram` each: pooled slots with `TryReserve`, `TryAdd`/`TryRemove` with
  generation `AgentHandle`s (stale handles are refused), deferred teardown, restart in place on
  root completion (`RootCompletionPolicy.Restart`, default) or `Stop`, `ThinkSchedule`
  (`Every(interval, phase)`, `Spread(interval)`), `TryWake`/`TryWakeMany`, and per-step read-only
  `GetRootCompletions()`/`GetFaults()`. Time is `tick × TickMicroseconds`. A tick that does not
  increase is refused and changes nothing, and calls made during a step return `Busy`. After
  warm-up, `Step` allocates nothing and does not log. Due agents run as one agent-frame job per
  group (`UseJobs`, from `MinimumJobBatch` = 64 due agents) or in place, with identical results
  for any job worker count. Instance ids are container-local and random streams are seeded from
  `RandomSeed` per `time-and-random-v1`. G1 runs trees whose generated nodes use blackboard keys
  only; inputs, commands and actions come in G2/G3, and emitted effects are discarded. See
  `Documentation~/simulation-container.md`.
- Internal: `ProductionTreeHost` now delegates to a MonoBehaviour-free `ProductionTreeAgentCore`.
  The host's behaviour is unchanged.
- Known issue found (unchanged): hosts leave random streams zeroed, so a tree with
  `aibt.stdlib.random-condition` (allowed only with nondeterministic nodes enabled) fails its first
  update under `ProductionTreeHost`/`NativeTreeAgent` (`Planning~/Evidence/P10-009/README.md`,
  section 3).
- Measured (IL2CPP, 500 agents, `Spread(5)`): `Step` median 57 µs (`shared-loop16`) and 317 µs
  (`shared-reactive16`) with 23 job workers, 0 bytes per tick; the frame costs 0.34× and 0.53× that
  of `NativeTreeAgent` with `UpdateCadence` 5.

### Simulation container design (P10-008, documentation only)

- Proposed `ADR-P10-008`: a GameObject-free `TreeSimulation` container stepped by the game with
  `Step(tick)`, with groups per program, pooled agent slots with generation handles, think
  rate/phase/wake, bulk inputs through the existing snapshot bindings, struct commands through the
  existing command bindings, and externally completed actions with cancel events. It includes the
  status of each requirement with code evidence, the ABI impact (no ABI v2 change), owner
  decisions, the proof plan and stages G1–G5. Review document for the game team:
  `Documentation~/proposals/simulation-container-review.md`. No code, API or behaviour changes.

### Automatic JobsAgentFrame selection (P10-007)

- **Default behaviour change.** A `ProductionTreeScheduler` now runs hosts whose profile forces no
  policy (`SchedulingProfile.Normal` and other custom profiles without `ForcedPolicy`, so every
  `NativeTreeAgent` by default) as `JobsAgentFrame` when they form a contiguous run, in the frame's
  due order, of at least 64 job-eligible hosts with the same profile and catalog (with
  `NativeTreeAgent` the catalog is per tree asset, so: 64 consecutive agents of one tree). Shorter
  runs stay `Immediate`. Results, traces, effects and their order are unchanged; frame entries of a
  selected host report `SelectedPolicy = JobsAgentFrame` and
  `SelectionReason = AutomaticAgentFrameRun`. A host the job cannot run (no generated dispatch, a
  catalog generated before P10-004, a `StepBudget`) keeps its policy without a warning. Automatic
  selection applies only with the default unbounded scheduler budget.
- **Opt out:** `ProductionTreeScheduler.AutomaticAgentFrameSelection = false` restores the previous
  behaviour exactly. Profiles that force a policy (including `SchedulingProfile.JobsAgentFrame`) are
  unaffected by the selection.
- **Short batches run in place.** Under `JobsAgentFrame` (forced or automatic), a batch of
  consecutive job-eligible hosts shorter than `ProductionTreeScheduler.AgentFrameMinimumBatchSize`
  (default `DefaultAgentFrameMinimumBatchSize` = 64) runs on the main thread exactly as `Immediate`
  does and schedules no job; its entries report `SchedulerFrameEntry.AgentFrameRanInPlace`. Set the
  minimum to 1 for the previous forced behaviour (a job for every batch).
- New public API (additions only): `ProductionTreeScheduler.AutomaticAgentFrameSelection`,
  `AgentFrameMinimumBatchSize`, `DefaultAgentFrameMinimumBatchSize`,
  `SchedulerFrameEntry.AgentFrameRanInPlace`, `SchedulerSelectionReason.AutomaticAgentFrameRun`.
  Measurements: `Planning~/Evidence/P10-007/README.md`.

### Unity 6000.6 compatibility (P10-006)

- The package compiles on Unity 6000.6.9f1: `Editor/Graph/BehaviorTreeImporter.cs` selects the
  `EntityId` overload of its `OnOpenAsset` handler from 6000.6 instead of 6000.7 (conditional
  compilation; 6000.7 and newer compile the same code as before). All AIBT test assemblies give the
  same results on 6000.6.9f1 as the last 6000.7.0b1 baseline (`Planning~/Evidence/P10-006/README.md`).

### Performance measurement (P10-005)

- Measured the native policies across 1–20 000 agents, job worker counts, mixed populations and
  `UpdateCadence` (IL2CPP Player, Unity 6000.6.9f1): `JobsAgentFrame` is at least 10% faster than
  `Immediate` on the main thread from 20–30 agents for simple trees and from 1 agent for a reactive
  tree; below that it costs 3–17 µs more per frame. `Documentation~/performance.md` has a new
  "Which policy when" table. No runtime behavior changed; defaults and the automatic selector are
  unchanged (`Planning~/Evidence/P10-005/README.md` has the measurements and a proposal for
  automatic selection).
- Known issue: the package at this version does not compile on Unity 6000.6.9f1
  (`Editor/Graph/BehaviorTreeImporter.cs` uses `AssetDatabase.GetAssetPath(int)`, an error there;
  the `EntityId` overload is selected only from 6000.7). Fixed in P10-006.
- Benchmark harness: scenario sets for small N, scale, worker count, mixed populations and cadence;
  process and main-thread CPU time per scenario; `Invoke-Perf.ps1 -Set` and `-JobWorkers`.

### Performance (P10-004)

- New opt-in policy `SchedulingPolicy.JobsAgentFrame`: one Burst job work item runs one native
  agent's whole update, node bodies and dispatch framing included. Opt in with
  `nativeTreeAgent.Profile = SchedulingProfile.JobsAgentFrame;` (before the agent is enabled); no
  `SchedulerJobsCapabilities` are needed. Defaults and the automatic selector are unchanged. In an
  IL2CPP Player (quick benchmark, 23 job workers) main-thread time per frame is 2.1–2.2× lower than
  Immediate for a one-leaf tree, 2.9–3.5× for a looping 16-node tree, 2.2–2.3× for a tree with a
  fact and an effect and 10.7× for a reactive tree with 46 dispatches per update. Traces, statuses,
  effects in order and blackboard bytes equal Immediate frame by frame
  (`JobsPolicyEquivalenceTests`).
  - Catalogs generated by this version (rebuilt `Analyzers/AIBT.CodeGen.dll`) carry a Burst
    agent-frame job; recompiling regenerates them. `ExpectedPublicAbiV2.txt` is unchanged.
  - Agents the job cannot run fall back to Immediate (or Budgeted) in their due position, with one
    warning per host and `SchedulerFrameEntry.AgentFrameFallback` naming the reason
    (`AgentFrameFallbackReason`): no generated dispatch, a per-host `StepBudget`, or a catalog
    generated before this version.
  - New public types in `AIBT.AgentFrame` are generated-code infrastructure, not an application API.
- New per-update step limit for every native policy: `ProductionTreeHost.MaximumStepsPerUpdate`
  (default `DefaultMaximumStepsPerUpdate` = 1 000 000). A tree that does not yield within the limit
  in one update (for example a repeater over an instant leaf) now fails its host with
  `NativeRuntimeDiagnosticCodeV1.NativeUpdateStepLimitExceeded` instead of blocking the frame.
  This is the only change to existing behavior; no measured tree comes near the default.

### Performance (P10-003)

- Native dispatch framing is cheaper on the main thread. In an IL2CPP Player (quick benchmark,
  interleaved before/after) a `NativeTreeAgent` frame takes 40–58% less main-thread time under the
  default Immediate policy and 31–78% less under `BatchedJobsSameFrame`; for example one waiting
  leaf at N = 1000 went from 3.7 to 2.0 µs per agent and a looping 16-node tree from 8.1 to
  3.8 µs. Tick order, statuses, traces, effects, diagnostics, defaults, public API and ABI v2 are
  unchanged; `JobsPolicyEquivalenceTests` compares every frame with digests recorded before the
  change.
  - The Burst bridge reads the dispatch backing by reference instead of copying it (≈0.5 KB) on
    every access, and its internal getters no longer make defensive copies of the carrier structs.
  - A dispatch workspace validates the immutable catalog metadata once instead of on every
    dispatch; per-request checks still run every time.
  - The grouped (`BatchedJobsSameFrame`) path no longer compares every pair of live values across
    all members, nor scans all members for each command's owner. A tree with facts and effects at
    N = 5000 went from 81 ms to 17.5 ms per frame and is no longer slower than Immediate.
- Fix: a `ProductionTreeScheduler` destroyed during a `BatchedJobsSameFrame` round now releases
  the round's Persistent storage when the round unwinds (P10-002 leaked it).
- Add a warmed-up Immediate zero-allocation test through `NativeTreeAgent`, per-step dispatch laps
  in the opt-in frame profile (`AIBT_SCHEDULER_FRAME_PROFILE`), and the profiling aids
  `Tools/profile.py` (step table) and `Tools/instrument_bridge_laps.py` in the Player harness.

### Performance (P10-002)

- `BatchedJobsSameFrame`: the group driver reuses its round storage across frames
  (`NativeBatchedLifecyclePoolV1`, internal) and schedules one Burst parallel-for job per round
  instead of one job per lane. A warmed-up frame of non-restarting trees allocates 0 B (it was
  ≈1.1 KB per agent tick for one waiting leaf). Main-thread time per frame in an IL2CPP Player
  dropped by 11–41% on the quick benchmark. Tick order, statuses, effect order, traces and
  diagnostics are unchanged; `JobsPolicyEquivalenceTests` compares Immediate and
  BatchedJobsSameFrame frame by frame.
- Add `Invoke-Perf.ps1 -Quick` (20 cases with a baseline comparison) and an opt-in frame profile
  (`AIBT_SCHEDULER_FRAME_PROFILE`, compiled out otherwise) to the Player harness.
- Draft ADR `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md` (Proposed) for advancing a
  whole agent frame inside one job.

### Performance (P10-001)

- Managed layer: a warmed-up `BehaviorTreeAgent` tick no longer allocates, including reactive
  re-evaluation, parallels, restarts and blackboard `Set`/`Get`/`TryGet` from game code
  (fixed-string values excepted). Execution order, statuses, traces and diagnostics are unchanged;
  `ManagedTickAllocationTests` guards it. In an IL2CPP Player (stripping High) 1 000 agents with a
  15-node tree went from 3.8 ms to 1.2 ms of main thread per frame and from ≈430 B per agent-tick
  with GC every ~14 frames to 0 B and no GC; per-node handler lookups are resolved once per node.
- Add `BehaviorTreeAgent.StaggerIntervalTicks` (off by default): Interval agents keep individual
  phases so agents spawned together do not tick on the same frame; every tick receives real time.
- Native layer: registering and unregistering a host with `ProductionTreeScheduler` and
  `ProductionTreeWorld` is O(1) (it was a scan of all hosts, O(n²) for n spawns or restarts);
  restarting 20 000 `native-shape15` agents went from 578 ms to 315 ms per frame. A participant that
  leaves the world inside an effect handler no longer makes the next participant's effects wait a
  frame.
- `BatchedJobsSameFrame`: `NativeBatchedLifecycleOwnerV1` checked lane uniqueness by comparing every
  pair each round; it now uses a set (same checks, same failures).
- A `NativeTreeAgent` whose profile forces a Jobs policy without `SchedulerJobsCapabilities` on its
  world's scheduler now faults with a message naming `ProductionTreeScheduler.SetJobsCapabilities`.
  Every scheduling profile is tested through the facade.
- Add `Documentation~/performance.md` (measured cost of both layers) and the Player harness in
  `Benchmarks~/Phase10/PlayerPerf/`.

### User guide and acceptance (P9-006)

- `README.md` is now a user guide: five-minute starts for the managed (`BehaviorTreeAgent`) and
  native (`NativeTreeAgent`) layers, exchanging data with game code, the graph editor, a standard
  node table, choosing a layer and known limits. Its code is compiled and checked against the
  sources by `UserGuideSnippetTests`. The phase history moved to `Documentation~/roadmap.md`.
- Generated API reference now covers `AIBT.Agents` (`Documentation~/generated/api-reference-agents.md`).
- `AIBT2043` names the key and reads as an input-key note ("Input key 'k': no node in this tree
  writes it ... game code sets it"). Keys a managed node binds through a `blackboard-key`
  parameter are no longer reported, because such a parameter may be written by the node. Severity
  (Info) and the rule for manifest access declarations are unchanged.
- Add a build step (`AibtLinkXmlProcessor`, an `IUnityLinkerProcessor`) that preserves every
  `[BtNode]` class and `[AibtCatalogSet]` catalog in a Player built with managed code stripping.
  Without it a Windows IL2CPP Player with stripping High could not compile any managed tree
  (standard and project nodes were stripped) and `NativeTreeAgent` could not resolve its catalog.
- Add `AIBT.PlayMode.Tests` with a Play Mode test for `aibt.unity.nav-move-to` path following.
- `Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` copies `Agents/` and `Analyzers/` into its
  isolated project and dumps `AIBT.Agents` and `AIBT.StandardNodes` too.
- `McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`
  compares with the project node catalog the tool uses; its old expectation missed the native
  standard shard and failed in every project.

### Native agent facade (P9-004)

- Add `NativeTreeAgent`: assign a prepared `ProductionTreeAsset` and press Play. It shares one
  program per asset and catalog between agents, creates its host, joins a world (an assigned one or
  a shared `AIBT Native World`) and restarts the tree by `NativeTreeRestartMode`.
- `SetFact(key, value)` writes typed Tree-scope facts at the world's safe boundary and keeps them
  across restarts; `OnEffect(name, handler)` receives typed effects by command name. A throwing
  handler is logged and does not block later effects. `Completed` and `Faulted` report results.
- Prepared runtime assets record their source and catalog. They are re-prepared automatically
  after the source tree is saved or the catalog changes; a stale asset is marked in its inspector
  with a Prepare Again button.
- The graph window debugs a selected `NativeTreeAgent` live in Play Mode (active path, finished
  nodes, root result).
- The First Agent sample uses `NativeTreeAgent`: its gameplay script shrank from 65 to 32 lines,
  and its setup menu creates a runnable demo scene.
- The package Burst shard and its nodes are now named `aibt.native.*` (`aibt.native.standard`,
  `aibt.native.bool-is-true`, ...); `aibt.stdlib.*` stays for nodes that run on both executors.

### Burst node authoring (P9-005)

- `Enter`, `Abort` and `Exit` are optional on a Burst node; an omitted one does nothing. A declared
  callback must still have the exact signature.
- Add short declarations in `AIBT.Native`: `[AibtAction("id")]` / `[AibtCondition("id")]` with
  defaults and configuration/memory types taken from `Tick`, `[AibtParam]` and `[AibtMemory]` with
  the value type taken from the field, `[AibtKey]` (access from the handle, Tree scope by default)
  and `[AibtCommand]`. The generator expands them into exactly the long `AIBT.Burst` attributes, so
  both forms produce identical metadata. The long form, its generated output and ABI v2 are unchanged.
- The First Agent `MoveNode` uses the short form and declares only `Tick`.

### Standard node library (P9-002)

- Add 38 standard managed `aibt.unity.*` nodes so a common agent needs no custom node: waits
  (random interval, until a flag, frames), blackboard set/copy/increment/toggle/compare-keys/
  is-set for Bool, Int32, Float32 and Vector3, random float/int/point and distance, movement
  (rotate-towards, turn-by, move-direction, keep-distance), optional NavMeshAgent nodes
  (nav-move-to, nav-stop), Animator trigger/bool/float, set-active, destroy-self, get-position,
  has-reference, play-audio, raise-event, line-of-sight, random-chance, a shared cooldown
  (start-cooldown / is-cooldown-ready) and time-since-changed. All are main-thread nodes.
- Nodes that use a Unity module (AI, Physics, Animation, Audio) compile only when that module is
  enabled, through version defines in `AIBT.Agents.asmdef`.
- `BehaviorTreeAgent` gets a **References** list: nodes address other scene objects by name
  (`reference` parameter; empty means the agent itself), and code can call `SetReference`.
- `BehaviorTreeAgent.Events`: trees raise named events; gameplay code subscribes by name, or wires
  **Event Responses** (UnityEvent) in the inspector.
- Add `BbAnyKey`, a key parameter of any type, and `BtNode.SecondsSinceChanged`,
  `ResolveGameObject`, `ResolveComponent<T>` and `WarnOnce` for node authors.
- The node search groups conditions and actions by category.
- The generated node catalog (`Documentation~/generated/node-catalog.md`) now lists the
  standard managed nodes alongside the built-ins; empty "when to use" / "when not to use" lines
  are omitted.
- Add a package Burst shard `aibt.native.standard` (assembly `AIBT.StandardNodes`):
  `bool-is-true`, `float-at-least`, `set-float` (blackboard through generated bindings) and
  `wait-microseconds` (host clock). A project `[AibtCatalogSet]` can list it next to its own
  shards. The nodes are native-only: the reference executor cannot read `GeneratedHandle`
  bindings, so they have no managed counterpart and `BehaviorTreeAgent` cannot run them.
- Simple Agent sample: the wanderer and most of the guard use standard nodes (random-point,
  wait-random, set-active, raise-event, look-at, turn-by), a new scout uses a scene reference,
  line-of-sight and keep-distance, a beacon reacts to the guard's `spotted` event (raised at
  most every 3 s through the cooldown nodes), and one custom node remains as the example.

### Graph editor

- Restyle the graph: grid, minimap, cards with kind accent, readable title, parameter summary,
  child-order and problem badges; top-down default layout from the deterministic layout service.
- Edit from the canvas: context menus, searchable node creation (context menu, Space, edge dropped
  on empty canvas), drag an edge to connect or reparent, Delete, Ctrl+S, Ctrl+Z/Ctrl+Y, Ctrl+D, F.
- One toolbar, one Save for tree and layout, one undo history shared with Unity's Edit > Undo, and
  incremental graph updates. Opening a tree never shows a modal dialog; a banner or a second
  window protects unsaved work.
- Typed inspector with contract defaults and type-filtered blackboard-key popups, a Blackboard
  panel and a clickable Problems list. Managed trees are listed as Valid, not Errors.
- Live debugging of a selected `BehaviorTreeAgent` in Play Mode: running nodes pulse,
  success/failure flash and fade, the active path is highlighted and blackboard values update.

### Managed agent layer

- Add `BehaviorTreeAgent`: assign an `.aibt` tree to one component and press Play. It compiles
  the tree once per asset and runs it on the existing reference executor, with automatic restart
  and no generated catalog, runtime asset or host wiring.
- Write nodes as ordinary classes: `BtAction` and `BtCondition` with `[BtNode]`, public fields as
  parameters and `BbKey<T>` fields as blackboard keys. Nodes may use the agent's GameObject.
- Read and write the blackboard by key name from gameplay code through `AgentBlackboard`.
- Add opt-in condition interrupts (`interrupt: self | lower-priority | both`) so a memory selector
  or sequence chooses again when a condition's answer changes, without restarting a running
  branch every tick the way a reactive composite does.
- Add starter `aibt.unity.*` nodes (log, wait-seconds, succeed, fail, idle, blackboard set and
  compare, move-towards, look-at, is-within-distance) and the importable Simple Agent sample.
- Import `.aibt` as a Player-loadable `BehaviorTreeSource`. Add managed-only node parameter types
  `int32`, `string` and `blackboard-key`; Burst manifests and existing bytes are unchanged.
- The reference Preview window explains that a tree with `BtAction`/`BtCondition` nodes runs in a
  `BehaviorTreeAgent` in Play Mode instead of reporting a compilation error.

### CLI and MCP

- Share project node contracts/policy across discovery, authoring and validation; refresh an open
  graph's catalog without losing drafts/Undo/layout. Preserve explicit reference/native backends.
- Add command schemas, native catalog inspection and hash-checked runtime asset preparation,
  plus read-only host/trace snapshots with incomplete-history disclosure. Resolve documentation
  from actual UPM package roots and generate project docs outside package caches.
- Replace inconclusive node scaffolds with source-controlled expected-outcome native cases.
  Verify scalar inputs/configuration and optional exact effects; expose behavior proof separately
  from dispatch proof. Applied Burst nodes are runtime-capable; case data stays Editor-only.

- Add a command-driven CLI and shared MCP connection client, explicit on-demand Editor bridge
  startup/recovery, health checks, bounded waits and one bridge owner across window/reload/CLI.
  Existing tools and permissions remain available; unknown mutation outcomes are never replayed.

### Consumer workflow

- Prepare a catalog-pinned compiled runtime asset in the graph Editor; create one explicitly owned
  `ProductionTreeProgram` shared by multiple hosts without runtime JSON compilation or reflection.
- Add finite Float32 node parameters throughout manifests, generation, validation, packing and the
  inspector, with an additive ABI v2 configuration reader. Existing contracts retain their bytes.
- Expose idempotent permanent host `Stop()` using existing cancellation/disposal semantics.
- Share the authoring graph with preview and verified selected-host trace; preserve layout and
  detach on source changes. Drag ports to connect/reparent with explicit semantic child order.
- Add the importable First Agent sample with Editor preparation, typed input, movement effects,
  source debugging and explicit shutdown. Fix selective asset opening on current Unity versions.

### Fixed

- Align generated-v2 blackboard default blobs for mixed-width values in all scopes and the
  semantic projection. Bool followed by Float32/Float3/Int64 no longer fails native preparation
  or exposes unaligned defaults; canonical ordering and zero padding are preserved.

- Reference preview now isolates behavior state per node, executes built-in leaves, and supports
  project reference providers. Built-in Wait keeps its counter in node memory.
- Integration fixture lookup works when AIBT is installed as a Unity package.

- P7-040: V1 native hot reload initializes declared Tree-blackboard defaults and migrates compatible
  built-in live values by stable key across slot relocation. New/incompatible keys use new defaults;
  subtree exclusions preserve shared Tree values and full restart uses defaults. Registered values
  without V1 schema proof default conservatively. Production-host/P7-039 storage is outside this fix.

### Added

- Experimental graph editing supports draft creation, structural edits, separate semantic and layout
  history, project node discovery, tree-scoped blackboard variables and generated-node bindings. An
  explicit native compilation check reports compiler diagnostics without claiming Player execution.
- The trace timeline can attach to any selected bootstrapped production host. Source highlighting
  remains unavailable until the matching program and graph are supplied explicitly.

- Release-Player evidence for Swarm Arena and its consumer UX/node-library review: the completed
  P7-035 Windows x64 IL2CPP/Burst matrix remains the only performance source, while P7-036 verifies
  the sample's onboarding, graph/layout, scheduler/debugger and MCP custom-node paths. No default
  scheduler/profile behavior or sample gameplay node was promoted or changed. See
  `Planning~/Evidence/P7-035/` and `Planning~/Evidence/P7-036/verification.md`.

- `ProductionTreeWorld` is an optional Unity-facing frame bridge for a shared
  `ProductionTreeScheduler`: ordinary game code stages safe external Tree-scope facts and receives
  bounded, ordered, committed typed effects without reading native command buffers or handling Jobs.
  Generated catalogs now describe supported `EffectCommand` bindings as stable `Int32`, `Float32` or
  `Float3` effect descriptors. See `Planning~/Evidence/P7-041/`.

- Importable `Samples~/SwarmArena/` gameplay showcase: one generated reactive tree visibly patrols,
  pursues a beacon, flees danger, attacks with instance-local cooldown and returns to patrol. It uses
  public external writes/effect delivery, one shared scheduler with Normal and PipelinedJobs agents,
  deterministic scripted 64/256/512 population phases, instanced state-group rendering, public
  scheduler/world telemetry and the existing trace debugger. This is not a performance claim; P7-035
  owns Player measurement. See `Planning~/Evidence/P7-034/`.

- Ordinary C# code can now write one live external value per frame into a running native tree
  instance's own Tree-scope blackboard, through new `ProductionTreeHost.TryResolveExternalTreeWrite<T>`/
  `TryWriteExternalTreeValue<T>` — gated on no node in the tree ever declaring Write access to the
  key, available only for a host bootstrapped through the generated-catalog dispatch overload.
  `TreeValidator` gained a new Info diagnostic, `AIBT2043 TreeScopeSlotNeverWritten`, flagging a
  declared Tree-scope key eligible for this. See `Planning~/Evidence/P7-039/README.md`.

- One optional population-level `ProductionTreeScheduler` replaces per-tree scheduling choices:
  registered `ProductionTreeHost` instances share one deterministic due/budget/policy decision built
  from stable `SchedulingProfile`s (built-in `Normal`/`Interactive`/`Background` presets, or custom
  project/runtime profiles) and an explicit `Unbounded`/`Fixed`/`Provider` global time allowance.
  Selection stays the existing deterministic `Auto` rule; a forced `BatchedJobsSameFrame` groups
  compatible agents through real generated-node dispatch, and a forced `PipelinedJobs` now drives that
  same real dispatch across scheduler-frame boundaries under explicit per-profile/capability opt-in.
  A bounded, allocation-free per-registration snapshot exposes every decision, estimate, budget and
  deferral for debugging. See `Planning~/Evidence/P7-033/README.md`.

- Grouped generated-dispatch (`BatchedJobsSameFrame`/`PipelinedJobs`) no longer allocates per wave: a
  scheduler-owned workspace, keyed by catalog and profile reference identity, stays warm across
  repeated runs with a fixed capacity derived from the registered cohort; a request beyond capacity
  fails structurally without committing partial participant state. See
  `Planning~/Evidence/P7-038/README.md`.

- Reproducible scheduling benchmark report and chart covering all 42 comparable Windows/Android
  Player points for plain loops versus same-frame Unity Jobs, with source hashes and explicit
  disclosure of the unmeasured pipelined policy.

- Two readable production-node showcase trees demonstrate timed sequence and deterministic
  parallel/decorator behavior, each with a committed graph layout and verified execution outcome.

- `AIBT Graph` now supports pan, zoom, node dragging, click and box selection; it reads existing
  layout positions and shows readable titles while keeping semantic editing disabled.

- Reproducible Windows IL2CPP tree-count size comparison: 1/100 authored trees, actual Player
  payload verification and file inventories. The measured +36,028-byte delta is serialized data
  and resource metadata; code is byte-identical. See `Planning~/Evidence/P7-026/` for scope limits.

- MCP node compilation now uses an explicit, domain-reload-persistent `attemptId` and an Editor
  compilation request. Legacy log-offset checks require a new start/check sequence. Apply validates
  Assets containment and rejects link/reparse ancestry. Background TCP test dispatch uses owned
  native storage; successful apply clears its staging-only catalog so subsequent compilation works.
- SameFrame and Pipelined controllers retain scheduled ownership after rejected completion buffers,
  allowing a valid retry without replay. Completed lane failures preserve their cleanup path.

- Document migration preserves blackboard, description, revision, scope contracts and bindings.
  Native hot reload preserves compatible cooldown state and cancels the previous active child
  path before traversing reordered children. See `Planning~/Evidence/P7-029/`.
- Full-lifecycle `ProductionTreeHost` dispatch with callback reasons and injected clock, retained
  failure diagnostics, and a per-frame step budget. The original Tick-only overload remains supported.
- Production-host regression coverage for terminal stopping, clock/decorator boundaries, budget
  resume identity, cancellation/disposal and callback failure. See `Documentation~/production-host.md`.

- Production host no longer starts updates after a terminal root or supplies a permanently zero
  clock. Budgeted frames preserve logical-update inputs; destruction cancels active work before
  releasing native storage. Trace records retain actual leaf exit/abort reasons and budget events.

- Initial repository and Unity package structure.
- Architecture, scope, editor, AI/MCP, benchmark, testing, and roadmap documentation.
- Strict canonical JSON contracts and schemas for semantic trees, node manifests, policies, and behavior cases.
- Normative v1 contracts for execution, update phases, blackboards, async commands, determinism, compiled programs, and platform backends.
- Agent execution workflow, dependency-aware work packages, Definition of Done, and atomic Phase 0–1 work items.
- Immutable authoring and compiled-program models, structured diagnostics, node registry, semantic validation, and deterministic reference compiler.
- Explicit-stack reference executor with lifecycle safety, memory/reactive/parallel composites, decorators, blackboard observers, commands, async completions, and deterministic step budgeting.
- Backend-neutral behavior-case runner and an end-to-end golden semantic slice covering canonical parse, validate, compile, and execute.
- Repeatable static, Unity compile, EditMode, Android ARM64 IL2CPP, and Unity Web verification entrypoints with sanitized evidence.
- Generated ABI v2 Burst node dispatch, public analyzers, deterministic catalog
  prebinding, and a public Condition/Action package sample.
- Fixed-capacity native program/state owners, Tree/Agent/Shared blackboards,
  snapshots, commands/completions, diagnostics/trace, lifecycle/composites,
  observers, async actions, budgeting, and same-frame batched Jobs scheduling.
- Native/reference golden equivalence, initialized zero-GC/lifetime gates,
  non-Development Windows x64 IL2CPP/Burst Player evidence, Android ARM64
  IL2CPP/Burst AOT evidence, and Chrome/Firefox Web conformance.
- `UnityEditor.Experimental.GraphView`-based editor components: read-only graph
  adapter, deterministic auto-layout, manual organization and layout
  persistence (groups/notes/reroutes), gated semantic editing, validation UX,
  reference-oracle-backed preview, read-only native execution debugger
  attachment, and a trace timeline view; proven layout/semantic isolation and
  large-graph (up to 2000 node) interaction performance measurements. Not yet
  wired into one live editor window.
- Fixed-policy scheduling cost curves, a calibrated work-estimation/batching
  model (recalibrated against real Windows x64 and Android ARM64 Player data,
  not Editor batchmode), a `PipelinedJobs` executor, and an `Auto`
  policy-selection heuristic with full explainability; `Auto` measured against
  fixed policies with underperformance disclosed rather than tuned away.
  Runtime autotuning evaluated and rejected (`OQ-006`) in favor of the static
  calibrated model. Real, non-Development Player benchmark evidence on Windows
  x64, Android ARM64 (physical device), and single-thread Web. No performance
  default, regression threshold, or supported-hardware-class claim adopted.
- Hot reload for the reference-executor backend (`OQ-007` resolved): a
  compatibility classifier localizing structural changes to the smallest
  necessary restart region, safe full restart, and idle-instance compatible
  state migration (memory/generation/cooldown flags/blackboard) built as one
  construct-fresh-and-selectively-copy mechanism keyed by stable authoring
  node ID, never by compiled index or in-place mutation. An explicit,
  explained Editor hot-reload workflow shows the actual classification and
  strategy used for every reload, verified live in the Editor. Measured
  evidence (Editor batchmode and a real, non-development Windows x64
  Standalone Player) shows compatible migration costs roughly half of a full
  restart at the same tree size. Native-backend hot reload does not exist;
  migration runs only against an idle old instance, falling back to full
  restart for a genuinely active one — both disclosed. No regression
  threshold or "acceptable reload cost" claim adopted.
- A real, working MCP integration: a standalone `dotnet` MCP server bridged to
  the Unity Editor, discovery/authoring/verification/test/benchmark/
  node-development tools, a fail-closed permission model covering 8
  categories, custom MCP tool providers discovered via inversion of control
  with no AIBT-side reference to the consuming project, and generated agent
  documentation (node catalog, workflow guide, recipes, anti-patterns,
  versioned migrations stub) sourced from the same production data every
  tool uses. Every tool live-verified against a real MCP client, including a
  genuinely new custom node generated, compiled, tested, and applied
  end-to-end. Trace inspection does not exist (no production code wires a
  real running native tree into a trace channel yet), and a just-generated
  custom node is not yet discoverable through the same discovery tools —
  both disclosed, not silently assumed.
- Production hardening: real Burst-confirmed Profiler instrumentation on
  every native/reference hot path; a long-running/stress test suite (20,000
  -tick soak, 10,240-agent stress, repeated reload under load); a real
  node-contract migration mechanism (declarative rename/add-with-default
  rules, an MCP tool, and a non-blocking Editor notification window); real
  production trace wiring (closing the trace-inspection gap above) and
  per-project leaf-registration/discovery (closing the node-discoverability
  gap above); a generic native-dispatch translator; native-backend hot
  reload, including active-instance migration (not idle-only, unlike the
  reference executor); two new samples; a generated API reference covering
  100% of public member signatures across all 4 assemblies; and local-first
  release automation. The Phase 7 integration gate independently
  re-verified all of this against a clean detached snapshot and accepted it,
  with real gaps disclosed rather than smoothed over: a production
  Play-mode host remains a fully-decided but unbuilt design; the tree
  format's `v2` (Agent/Shared blackboard) is not yet the production
  default; `AIBT.Editor`/`AIBT.Mcp` stay explicitly experimental (a real,
  previously-undocumented breaking change was found in `AIBT.Mcp`'s own
  tool-surface history); and the generated API reference's type-summary
  inlining silently no-ops for a real UPM (`file:`/registry) consumer,
  found for the first time by the gate's own detached-harness technique.
  `1.0.0` has not been declared.
