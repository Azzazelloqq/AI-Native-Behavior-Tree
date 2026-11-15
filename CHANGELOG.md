# Changelog

All notable changes to this project will be documented here. The project follows Semantic Versioning while public APIs remain explicitly marked as experimental before `1.0.0`.

## [Unreleased]

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
