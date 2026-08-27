# ADR P5-001: hot-reload compatibility model — `OQ-007` resolution

- Status: Accepted 2026-08-27
- Date: 2026-08-27
- Decision ID: AIBT-023

## Context

`OQ-007` ("define what 'reload' means for a semantically changed tree with a live instance
mid-execution") was left open by both `P3-013` and `P4-009`. Before any restart or migration
mechanism could be designed, the real data-structure facts had to be established rather than
assumed:

- **Compiled node index has zero stability across recompiles.** `Authoring/Compilation/ReferenceCompiler.cs`'s
  `OrderNodes`/`IndexNodes` assign compiled index purely from a fresh pre-order DFS traversal of
  the authoring document, every compile. Any edit anywhere earlier in traversal order — even one
  that has nothing to do with a given node — shifts that node's compiled index.
- **Every live-state array, in both backends, is flatly indexed by that unstable index.**
  `ReferenceExecutionMachine`'s `_nodeMemory`/`_activationGenerations`/`_observerStates` and the
  native runtime's `NativeInstanceArenaOwner` generation table are all sized and indexed off the
  compiled program at construction time. There is no indirection layer today.
- **The native layer already hard-rejects cross-generation execution by design**
  (`native-runtime-v1.md`'s program-binding invariant, enforced via `AIBT4311`). There is no
  in-place rebind path, and this ADR does not propose adding one — it is a deliberate existing
  safety invariant, not a gap.
- **The only identities that survive a recompile unchanged** are the stable authoring `NodeId`
  (via `CompiledProgram.DebugMap`), blackboard `StableKeyId`, and async `OperationId`'s embedded
  node ID.
- **A Memory composite's own "which child am I on" state is a positional `uint` cursor**
  (`ReferenceMemoryCompositeHandlers.ReferenceCompositeDecision.CursorAfterAcceptance`), not a
  stable child identity — confirmed by reading the real handler code, not assumed.

A disposable spike (`Spikes~/HotReloadCompatibilityModel/`, archived from a temporary
`Tests/Editor/` location after a live Unity test run — 5/5 passed) proved a stable-`NodeId`-keyed
classifier against real `CompiledProgram` pairs produced by the real `ReferenceCompiler`, covering
all five `testing.md` change categories: parameter edit, insertion, removal, reordering, and
type change. The reordering case specifically proved the load-bearing fact this whole model exists
to address: two nodes' compiled indices differ before vs. after a reorder even though neither node
itself changed — see `Planning~/Evidence/P5-001/README.md` for the full spike output.

## Decision

**Reload is never an in-place mutation of a live instance's arrays. It is always: construct a
fresh instance bound to the new `CompiledProgram`, then selectively copy surviving live state into
it, keyed by stable authoring `NodeId`, never by compiled index.** This single mechanism — not
three independent ones — underlies every reload outcome:

1. **Per-node classification.** For every `NodeId` in the union of the old and new program's
   `DebugMap`:
   - Present only in **old** → `Dropped`. Any active async operation on it is cancelled per
     `async-and-commands-v1.md`'s existing idempotent-cancellation rule; its state is discarded.
   - Present only in **new** → `New`. No prior state exists; it initializes fresh.
   - Present in **both**, same `NodeTypeId` + `NodeTypeVersion` + instance-memory layout
     (`InstanceMemorySize`/`InstanceMemoryAlignment`/`MemoryLifetime`) → `Migrate`. Its live state
     (memory bytes, activation generation) copies from its old compiled index to its new compiled
     index, both resolved through the stable-`NodeId` map — never assumed equal.
   - Present in **both**, different type or version → `Incompatible`. Per
     `async-and-commands-v1.md`, treat exactly like an abort: cancel any active operation, discard
     this node's own state. (Type-and-version-equal-but-layout-differs is a defensive fallback case
     — correctly authored manifests derive layout deterministically from type+version+config, so
     this should not occur; if it ever does, classify `Incompatible`, never silently proceed.)
2. **Composite cursor rule.** Any Memory composite whose direct children's order changed
   (reordering, or an insertion/removal among its direct children) resets its own cursor to
   "not yet started this activation," even when every individual child migrates fine — the
   cursor's positional meaning does not survive a reorder. Reactive composites re-evaluate from
   the top every tick already and are not subject to this rule; `P5-002`/`P5-003` must confirm this
   empirically against `ReferenceReactiveCompositeRegistry`'s actual real state, not assume it from
   this ADR alone.
3. **Whole-reload decision.** If no node classifies `Incompatible` anywhere, the reload is
   **compatible migration**: every `Migrate`/`New`/`Dropped` node is handled per above, nothing
   restarts. If any node classifies `Incompatible`, **localize**: find the smallest subtree(s)
   containing every incompatible node by walking up each program's own children table, and
   restart only those subtree(s) fresh (their contained nodes are *not* migrated even if they
   would otherwise classify `Migrate`) while every node outside them still migrates normally.
   Localization is refused — falling back to **full-instance restart** — whenever it cannot be
   proven safe: the incompatible node is the root, or a shared-blackboard write from inside the
   localized region is observably read by a node outside it (`agent-shared-blackboard-v1.md`'s
   conflict-policy rules govern this check).
4. **Full restart is not a fourth mechanism.** It is this same decision applied with the
   localized region equal to the whole tree — the mandatory, always-available floor when
   localization fails or is not attempted at all.

### Correction this ADR makes to the original `P5-005`/`P5-006` card split

The original Phase 5 decomposition described "affected-subtree restart" (`P5-005`) and "compatible
active-state migration" (`P5-006`) as two separate mechanisms. They are not: both are the same
stable-`NodeId` diff-and-copy mechanism from item 3 above, differing only in which nodes are
excluded from the copy (none, for a pure migration; the localized incompatible region, for a
"subtree restart"). `P5-005` and `P5-006`'s Allowed-changes/Deliverables should be read as building
one shared mechanism with a parameterized exclusion set, not two independent implementations —
the integration owner should merge or resequence them accordingly before either starts
implementation, rather than building the same copy logic twice.

## Consequences

- `P5-002` builds the stable-`NodeId` identity/layout-signature map (`IdentitySignature` in the
  spike) as real production code, keyed off `CompiledProgram.DebugMap` + `Nodes` — no change to
  `compiled-program-v1.md`'s accepted format is required; the scheme is computed from existing
  data, not a new stored field.
- `P5-003` implements the per-node classifier and the localization walk from item 3, both proven
  constructible by the spike.
- `P5-004`/`P5-005`/`P5-006` all consume the same copy mechanism; the integration owner adjusts
  their card split per the correction above before implementation starts.
- `P5-007` must verify the composite-cursor-reset rule (item 2) does not silently reintroduce a
  semantic change disguised as a "compatible" migration — a reset cursor changes *when* a
  composite's children next execute, which is an observable behavior difference `P3-007`'s own
  layout/semantic isolation discipline would flag if applied here.
- No production code ships from this card. The spike (`Spikes~/HotReloadCompatibilityModel/`) is
  disposable, per this card's own Forbidden changes.

## Addendum (`P5-005`/`P5-006` implementation): migration is idle-only, by deliberate scope

Building the shared copy mechanism (`Runtime/HotReload/Migration/HotReloadStateMigration.cs`) found
`ReferenceFrame` (the active-traversal-stack element) has a `NodeIndex` fixed at construction and an
extensive set of decorator/parallel/repeater/cooldown-specific mutable fields — remapping a live
frame to a new compiled index means reconstructing it field-by-field, a much larger and more
failure-prone undertaking than copying `_nodeMemory`/`_activationGenerations` (which this ADR's item
3 assumed was the bulk of the work). Escalated to the owner before proceeding
(`AskUserQuestion`); decision: **migration only ever runs when the old instance is idle**
(`CaptureInspection().ActiveNodeCount == 0`, i.e. no frames on the stack) — it copies per-node
memory, activation generation, and cooldown-init flags (via two new internal
`ReferenceExecutionMachine` methods, `CaptureNodeState`/`SeedNodeState`) plus blackboard values
(via the existing `initialBlackboard` constructor parameter, already keyed by stable
`StableKeyId` — no new blackboard plumbing needed). Whenever the old instance is *not* idle, both
`P5-005` and `P5-006` fall back to `HotReloadFullRestart` entirely, exactly like an unsafe
localization does. Full mid-flight frame-stack migration remains real, disclosed follow-up work,
not attempted here — see `Planning~/Evidence/P5-006/README.md`.
