# Hot reload

This document consolidates Phase 5's contract. Every rule below already exists
as a decision scattered across `architecture.md`, `scope.md`, `roadmap.md`,
`decisions.md` (`AIBT-008`), `testing.md`, `benchmarks.md`, and the
`specifications/` normative documents; nothing here is a new decision. Where
a rule is genuinely undecided, this document says so explicitly rather than
inventing one -- see "Open questions" below.

## Scope

Hot reload replaces a running tree instance's compiled program with a newer
one, in the Editor or a running Player, without necessarily restarting the
whole game (`AIBT-008`, `scope.md`). It is included in scope; persistent
save/load of live execution state is explicitly excluded and is a separate,
unscoped feature (`AIBT-008`, `scope.md`). Hot reload never claims to
preserve state across incompatible changes; a safe restart is always the
fallback (`roadmap.md`).

The Editor owns hot-reload orchestration (deciding when a reload happens and
presenting its result to the user); Runtime/Authoring own the mechanism
(classifying compatibility and performing the reload itself)
(`architecture.md`'s Editor responsibility list).

## Identity and versioning

- **Node IDs are stable, opaque, and authoring-assigned.** Renaming a node's
  display name does not change its hot-reload identity
  (`data-formats.md`).
- **The debug/hot-reload map** associates a runtime node index with its
  stable authoring node ID, source path, and optional display metadata,
  separately from presentation layout. Release builds may strip display
  strings while retaining the stable IDs hot reload and diagnostics require
  (`specifications/compiled-program-v1.md`).
- Program versions and state-layout hashes classify whether two compiled
  programs are hot-reload-compatible (`roadmap.md`). Per `ADR-P5-001`, this
  is computed per-node from existing `CompiledProgram` data (`DebugMap`'s
  stable authoring node ID cross-referenced with each node's `NodeTypeId`/
  `NodeTypeVersion`/instance-memory layout) -- no new field is added to the
  accepted `compiled-program-v1.md` format. `P3-007`'s existing
  `CompiledProgram.Header.CompiledContentHash` (proven layout-vs-semantic
  isolation invariant, inherited via `Planning~/Evidence/P3-GATE/phase5-inputs.md`)
  remains the whole-program "did anything change at all" signal that decides
  whether to run this per-node classification in the first place.

## Compatibility classification

Resolved by `OQ-007`/`ADR-P5-001` (`AIBT-023`). A structural or parameter
change to a tree is one of the five categories `testing.md`'s "Hot-reload
tests" section requires coverage for -- parameter edit, insertion, removal,
reordering, type-version change -- and every node in the union of the old and
new compiled program's stable authoring node IDs classifies as exactly one
of:

- **`Migrate`** -- present in both programs with the same `NodeTypeId` +
  `NodeTypeVersion` + instance-memory layout (size/alignment/lifetime). Its
  live state (memory bytes, activation generation) copies across, re-keyed
  from its old compiled index to its new one via the stable node ID -- never
  by assuming the index is unchanged, since it almost never is (see below).
- **`New`** -- present only in the new program (an insertion). No prior
  state exists; it initializes fresh.
- **`Dropped`** -- present only in the old program (a removal). Any active
  async operation on it is cancelled per the idempotent-cancellation rule
  above; its state is discarded.
- **`Incompatible`** -- present in both with a different type or version.
  Treated exactly like an abort: cancel any active operation, discard this
  node's own state, restart it.

Reordering a Memory composite's direct children additionally resets that
composite's own running cursor (a positional `uint`, not a stable child ID —
confirmed against the real handler code), even when every individual child
node itself migrates fine; reactive composites re-evaluate from the top every
tick already and are not subject to this rule.

**Why compiled index cannot be the migration key**: `ReferenceCompiler`
assigns compiled node index from a fresh pre-order DFS traversal on every
compile -- it has zero stability across recompiles, and an edit anywhere
earlier in traversal order shifts every later node's index, even nodes the
edit did not otherwise touch. `ADR-P5-001`'s spike proved this concretely: a
plain child reorder changes both children's compiled indices while their
identities and compatibility verdicts stay unchanged.

## Reload strategies

Resolved by `OQ-007`/`ADR-P5-001`. There is exactly **one** underlying
mechanism, not three independent ones: construct a fresh instance bound to
the new compiled program, then selectively copy surviving live state into it
per the classification above. What differs between "strategies" is only
which nodes are excluded from that copy:

1. **Safe full restart** -- the exclusion set is the whole tree. Always
   available, always correct, no state preserved. The mandatory fallback
   when localization (below) cannot be proven safe or is not attempted.
2. **Affected-subtree restart** -- the exclusion set is the smallest
   subtree(s) containing every `Incompatible`-verdict node, found by walking
   up each program's own children table. Every node outside that subtree
   still migrates normally. Localization is refused (falling back to full
   restart) when the incompatible node is the root, or when a shared write
   from inside the localized region is observably read from outside it.
3. **Compatible active-state migration** -- the exclusion set is empty: no
   node classified `Incompatible` anywhere, so every node's own verdict
   (`Migrate`/`New`/`Dropped`) applies without any forced restart.

Because all three are the same mechanism, `P5-005` and `P5-006` build one
shared copy implementation with a parameterized exclusion set, not two
separate ones -- see `ADR-P5-001`'s "Correction" section.

## Interaction with async operations and commands

Hot reload applies the same idempotent-cancellation rule async operations
already use for abort: an active async node is cancelled (at most one
cancellation command, idempotent, cancelled state committed before the
cancellation command is appended) whenever its node identity, type version,
configuration compatibility, or memory layout is incompatible with the new
program (`specifications/async-and-commands-v1.md`). A late completion from
the old activation cannot reactivate or complete after that.

## Interaction with determinism (time and random)

A random stream is preserved across a reload only when the hot-reload
compatibility contract accepts the same node identity/version and
random-state layout; otherwise subtree restart applies, and the stream
re-derives the same way a genuinely new node would
(`specifications/time-and-random-v1.md`). Committed streams for abort,
cancellation, exit, observer evaluation, budget suspension/resume, rejected
context operations, failed/rejected frame completion, diagnostics, and trace
capture never advance from a reload alone.

## Interaction with the scheduler (Phase 4 inheritance)

Per `Planning~/Evidence/P4-GATE/phase5-inputs.md`:

- The four accepted execution policies and `Auto` are proven semantically
  equivalent to the reference oracle; a hot-reload path must not weaken that
  equivalence, or introduce a fifth execution path that bypasses it, to make
  reloading more convenient.
- `NativeWorkEstimatorV1`'s calibration state is keyed by caller (one
  estimator per compiled-program identity/population), not embedded in
  `CompiledProgram` -- structurally decoupled from reload identity either
  way. Whether an existing estimator instance is reset or carried over
  across a reload is this phase's own decision, not pre-made here.
- Runtime autotuning was rejected (`OQ-006`); no live-adapting scheduling
  state exists that a reload needs to migrate or reset. `Auto`'s shipped
  selection (`TrySelect`) is a pure function of its inputs each call.

## Interaction with layout and semantics (Phase 3 inheritance)

Per `Planning~/Evidence/P3-GATE/phase5-inputs.md`:

- `P3-007`'s proven invariant (every manual-organization action and
  auto-layout run leaves `CompiledProgram.Header.CompiledContentHash`
  byte-identical; a genuine semantic edit changes it) is hot reload's own
  change-detection signal for "does this edit require a live instance to
  reload at all" -- a layout-only change is provably a no-op for any live
  instance and never triggers a reload.
- `CompiledProgram` and its content hash are the same object whether
  produced for the managed reference oracle or the native executor
  (reconfirmed unchanged through `P3-GATE`'s own detached-harness
  regression) -- one artifact identity to track across both backends, not
  two.
- `P3-009`'s `ReferencePreviewDriver` (a public `AIBT.Authoring` facade
  crossing an internals-visibility boundary by explicit owner decision) and
  `P3-010`'s `NativeExecutionDebuggerSession` (read-only, no writer-lease
  required) are both working examples of the same cross-boundary-driving
  discipline a reload mechanism will likely need if it has to swap or
  inspect a running instance's compiled program from outside its own
  assembly.

## Editor workflows

The Editor surfaces reload as an explicit workflow, not a silent background
action (`architecture.md`). Users see structural and parameter changes
classified, and are told which strategy (restart vs. migration) was applied
and why -- the same explainability discipline `execution-and-scheduling.md`
already requires of the scheduler applies here: a reload decision is
explained, not merely made.

## Testing

`testing.md`'s "Hot-reload tests" section requires coverage of: parameter
edits, insertions, removals, reordering, type-version changes, compatible
state migration, incompatible subtree restart, full restart, and trace
explanation. Every category needs both a positive case (the expected
strategy is chosen and correctly applied) and a negative case (an
incompatible change is never silently migrated).

## Benchmarks

`benchmarks.md` lists hot-reload and debug-instrumentation overhead as a
required synthetic-scenario category, and compilation/import and hot-reload
cost as a required native-runtime metric. As with every Phase 4 benchmark,
no default, threshold, or "acceptable reload cost" claim exists until the
owner approves one per `Planning~/USER_ACTIONS.md` -- Phase 5 measures, it
does not adopt.

## Open questions

`OQ-007` (what "reload" means for a semantically changed tree with a live
instance mid-execution) is resolved -- see `ADR-P5-001`
(`AIBT-023`) and the "Compatibility classification"/"Reload strategies"
sections above, which state the decision directly.

- **A production per-project leaf-behavior registration mechanism** does not
  exist yet; every executable leaf anywhere in AIBT today is a Phase 1
  fixture or a built-in composite/decorator (inherited limitation from every
  Phase 3 evidence file, e.g. `Planning~/Evidence/P3-009/README.md`). Hot
  reload can be built and tested against that same fixed set, the same way
  Phase 3's preview/debugger/trace views were; it does not block Phase 5,
  but it does mean "hot-reload a real project's custom nodes" is not
  demonstrable until a leaf-registration mechanism exists (tracked as a
  disclosed limitation, not a new open question).
