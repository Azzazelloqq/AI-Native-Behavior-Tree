# ADR P9-004: Native tree agent facade and automatic preparation

- Status: Accepted by owner plan approval (Stage 4), 2026-11-15
- Decision ID: AIBT-046

## Context

Running one prepared tree on the native path needed four objects wired by hand: a
`ProductionTreeAsset`, a `ProductionTreeProgram` created from a catalog factory and disposed only
after every host, a `ProductionTreeHost` bootstrapped with an effect capacity, and a
`ProductionTreeWorld` participant that resolves external Tree-scope writes, flushes them at the
scheduler's safe boundary and decodes effects by stable hash. The First Agent sample spent 65
lines on this plumbing. The runtime asset also did not remember which source and catalog it was
prepared from, so a tree edit or a catalog change left a silently stale snapshot.

## Decision

1. **Assembly.** `NativeTreeAgent` and its internal program cache live in `AIBT.Runtime`
   (`Runtime/Integration/`), next to `ProductionTreeHost` and `ProductionTreeWorld`, because a
   component must be in a Player assembly and every type it composes is already there.
   `AIBT.Runtime` gains no assembly or package dependency. A separate assembly was rejected: it
   would need `InternalsVisibleTo` for the same internals and adds one more asmdef to every
   consumer. `AIBT.Runtime` grants `InternalsVisibleTo("AIBT.Editor")` so the graph debugger can
   read the activation path; no internal API is made public.
2. **Composition only.** The agent is a participant of the existing world. It does not change the
   scheduler, its defaults, execution semantics, ABI v2 or any existing public API. Hosts it
   creates are hidden components on the same GameObject (`HideInInspector | DontSave`).
3. **Program sharing.** Programs are cached by (runtime asset, catalog factory) and reference
   counted; the last agent to release a program disposes it. The cache is reset on
   `SubsystemRegistration`, so a disabled domain reload cannot leak programs between Play sessions.
4. **World.** An agent uses its assigned `ProductionTreeWorld`, or one shared
   `AIBT Native World` object created on demand (`DontDestroyOnLoad` in Play Mode).
5. **Catalog.** The agent resolves the catalog from the type name recorded in the asset by
   reflection and calls its generated `TryCreateRuntimeCatalog`. Code can assign
   `CatalogFactory` instead, which is required when stripping removes the catalog type.
6. **Facts.** `SetFact<T>(key, value)` caches one external-write handle per key and host. Values
   are written at the world's input boundary (`TryFlushInputs`), never while the tree runs. The
   last value of every fact is written again into a restarted or replaced host. A key the tree
   does not declare as a writable Tree-scope key of that type is reported once and ignored; the
   tree keeps running.
7. **Effects.** `OnEffect(name, handler)` registers a typed handler keyed by the effect's stable
   command hash. An exception in a handler is logged and the effect is consumed: one faulty
   handler must not block later effects (the raw host's policy of leaving a failed effect pending
   is unchanged for direct host users). Effects without a handler are dropped.
8. **Lifecycle.** `OnEnable` starts, `OnDisable` stops (host stopped and destroyed, world
   unregistered, program released). When the root finishes, `LastResult`, `CompletedRuns` and the
   `Completed` event are updated in `LateUpdate`, then `NativeTreeRestartMode` decides whether a
   fresh host starts (default `Always`). A host fault raises `Faulted` and stops the agent.
9. **Automatic preparation.** A prepared asset records its source GUID, source SHA-256 and catalog
   type. `ProductionTreeAssetStaleness` re-prepares linked assets in place when their source is
   imported (synchronously in the postprocessor, so it works while the Editor is in the
   background) and after every script reload when the catalog fingerprint changed. Failures are
   logged and leave the old snapshot, shown as stale in the inspector with a Prepare Again
   button. Assets prepared before this change are `Unknown` until prepared again.
10. **Live debugging.** The graph window can attach to a `NativeTreeAgent` in Play Mode. It reads
    the host's activation stack between frames and maps runtime node indexes through the
    program's debug map: active nodes pulse, nodes leaving the path flash, the root flashes its
    result. The trace channel is not used because it keeps only the first records of a session.

## Consequences

- One component plus a few lines of gameplay code run a native tree; the low-level path is
  unchanged and still documented for populations and custom scheduling.
- Live view limits: exit statuses of non-root nodes and node activations that start and end within
  one frame are not visible; native blackboard values are not shown; a host is not read while it
  is driving or has a pipelined job in flight.
- Resolving the catalog by type name works in the Editor; Player builds with stripping need
  `CatalogFactory` or a preserved catalog type. A Player build check is a separate card (P9-006).

## Amendment 2026-11-24 (P11-016 stage 2): the managed node rows are kept only when the Player uses `AIBT.Agents`

Context. Decision 5's stripping support (P9-006) writes one generated link.xml with a
`preserve="all"` row for every `[BtNode]` class, `[AibtCatalogSet]` catalog and `[AibtBurstValue]`
type in the Player's assemblies. The rows of the package's own managed nodes (`AIBT.Agents`) made
every Player keep `AIBT.Agents` and, through it, `AIBT.Authoring`, also a game that never uses the
managed layer. P11-016 stage 0c measured it in temporary copies of the package (raw files under
`C:\UnityProjects\_aibt_f14\size\`, report `P11-016-stage0-report.md`; evidence
`Planning~/Evidence/P11-016/README.md`):

- A native-only game (no code reference to `AIBT.Agents`) with the `AIBT.Agents` rows removed ships
  without `AIBT.Agents` and `AIBT.Authoring` at Minimal and at High: 2.86 MB less shipped at Minimal
  (2.30 MB less `GameAssembly.dll`), 2.24 MB less at High (1.87 MB). `Newtonsoft.Json` stayed,
  because `AIBT.Runtime` referenced it (removed by P11-016 item 4).
- With the rows removed, a managed game that uses standard nodes only from a scene (a
  `BehaviorTreeAgent` in a scene, no code reference to `AIBT.Agents`) fails at High: its node types
  are stripped (`AIBT2021`).
- With the rows written on an `<assembly fullname="AIBT.Agents" ignoreIfUnreferenced="1">` element,
  that managed game runs at High with a PASS verdict, and the native-only Player is as small as
  without the rows (within 2.6 KB at Minimal, 17.6 KB at High).

Decision.

1. The `AIBT.Agents` element of the generated link.xml always carries `ignoreIfUnreferenced="1"`: its
   rows apply when anything the linker keeps references `AIBT.Agents` (a scene or prefab with a
   `BehaviorTreeAgent`, code), and are ignored otherwise.
2. Rows of every other assembly, the project's own `[BtNode]`, `[AibtCatalogSet]` and
   `[AibtBurstValue]` types included, stay unconditional, as before.
3. One override restores the output before this amendment byte for byte: the scripting define
   `AIBT_LINKXML_KEEP_MANAGED_NODES` in the Player Settings of the build target. A scripting define is
   used because the package has no project settings asset and already uses defines for build options
   (`AIBT_SCHEDULER_FRAME_PROFILE`).

Consequences.

- A Player that uses only the native layer no longer ships `AIBT.Agents` and `AIBT.Authoring`.
- A managed game that reaches `AIBT.Agents` only through content the linker cannot see (for example
  a `BehaviorTreeAgent` that exists only in AssetBundle or Addressables content built separately)
  loses the managed node types, as any unreferenced assembly would. Such a game sets the define.
- The generated XML is covered per case by Editor tests, and the override's output is compared with
  the output before this amendment.
