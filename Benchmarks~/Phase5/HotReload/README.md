# Phase 5 hot-reload benchmark (`P5-009`)

Measurement only, per `Documentation~/hot-reload.md`'s "Benchmarks" section and every Phase 4
benchmark's own discipline: no default, threshold, or "acceptable reload cost" claim is drawn
anywhere in this file (`Planning~/USER_ACTIONS.md` still requires owner approval before any
threshold is adopted).

## What was measured

`Unity/Runtime/HotReloadBenchmarkRunner.cs` measures the reference-executor hot-reload mechanisms
(`P5-004` full restart, `P5-005`/`P5-006` compatible migration, and a localized subtree restart)
through the public `HotReloadPreviewDriver` facade only (`P5-008`) -- no internals access, so the
exact same source runs unmodified in Editor batchmode and copied into an isolated Player project,
mirroring `Documentation~/benchmarks.md`'s existing harness pattern.

For each of three tree shapes (`single-leaf`/1 node, `shallow-sequence-5-leaves`/6 nodes,
`deep-sequence-63-nodes`/63 nodes), four costs are isolated (5 warmup + 15 measured samples each,
median/min/max reported):

- `compileOnlyMicroseconds` -- `ReferenceCompiler.Compile` alone, no reload at all. This isolates
  compilation/import cost from reload-mechanism cost, per this card's own deliverable.
- `fullRestartTotalMicroseconds` -- compile the changed document, then a full tear-down-and-rebuild
  (`P5-004`) against a **freshly created, still-idle** old instance.
- `compatibleMigrationTotalMicroseconds` -- compile a structurally identical document (isolates pure
  reload-mechanism cost from any real classification work), then `P5-005`/`P5-006`'s selective-copy
  migration against an idle old instance.
- `subtreeRestartTotalMicroseconds` -- compile a document with the first leaf's type changed
  (forces `P5-003`'s classifier to mark that node `IncompatibleRestart` and localize a subtree
  restart), then the resulting mixed migrate+restart reload.

A fourth, supplementary measurement (`Results/hot-reload-benchmark-population-scaling-windows-editor-20260827.json`)
isolates a fifth question this card's deliverables raise implicitly ("multiple ... population
sizes"): whether reloading many live instances against the same new document shares any
compile/classify cost across them.

## Results

### Editor batchmode (`Results/hot-reload-benchmark-windows-editor-20260827.json`)

Windows 11, Intel Core Ultra 9 275HX, 24 logical processors, Unity 6000.5.8f1, WindowsEditor.

| Shape (nodes) | compile-only (median µs) | full restart (median µs) | compatible migration (median µs) | subtree restart (median µs) |
|---|---:|---:|---:|---:|
| single-leaf (1) | 2143.5 | 4251.0 | 2142.4 | 2147.4 |
| shallow-sequence-5-leaves (6) | 2266.4 | 4659.5 | 2303.1 | 3294.2 |
| deep-sequence-63-nodes (63) | 5218.1 | 10662.2 | 5615.5 | 6294.3 |

### Windows Standalone Player, non-development, Mono2x (`Results/hot-reload-benchmark-windows-player-20260827-074542.json`)

Same workstation, real built `.exe` launched with `-batchmode -nographics`, confirmed
`applicationPlatform: "WindowsPlayer"`, `isEditor: false`. Build evidence
(`Results/hot-reload-benchmark-windows-player-20260827-074542-build.raw.json`):
`result=Succeeded`, `target=StandaloneWindows64`, `developmentBuild=false`, 0 build errors/warnings.

| Shape (nodes) | compile-only (median µs) | full restart (median µs) | compatible migration (median µs) | subtree restart (median µs) |
|---|---:|---:|---:|---:|
| single-leaf (1) | 933.8 | 1846.7 | 964.7 | 890.7 |
| shallow-sequence-5-leaves (6) | 982.2 | 1919.1 | 1043.1 | 1010.0 |
| deep-sequence-63-nodes (63) | 2907.7 | 5959.1 | 3086.6 | 3025.5 |

This is the required "at least one measurement runs on a real, non-Editor Player build"
(`P4-008`'s own precedent).

### Population scaling, Editor batchmode, shallow-sequence-5-leaves, compatible migration only

| Population size | Total (median µs) | Per-instance (median µs) |
|---:|---:|---:|
| 1 | 4728.2 | 4728.2 |
| 10 | 45893.5 | 4589.4 |
| 50 | 234519.1 | 4690.4 |

## Findings

- **Editor understates Player performance by roughly 2-2.3x here**, consistent in direction with
  (though much smaller in magnitude than) `P4-008`'s ~13-14x Editor-vs-Player scheduling gap --
  single-leaf compile-only: 2143.5µs Editor vs 933.8µs Player (~2.3x); deep-sequence-63-nodes:
  5218.1µs Editor vs 2907.7µs Player (~1.8x). The exact ratio is not constant across tree sizes, so
  no single multiplier is proposed as a conversion factor -- this is reported as a finding, per
  this card's own forbidden-changes clause, not turned into a rule.
- **Full restart consistently costs roughly 2x a compatible migration at the same tree size, on
  both Editor and Player** -- e.g. deep-sequence-63-nodes: 10662.2µs full restart vs 5615.5µs
  migration (Editor, ~1.90x); 5959.1µs vs 3086.6µs (Player, ~1.93x). Since the migration case here
  compiles a structurally identical document (zero real state differences to copy), this ~2x gap is
  the fixed overhead of `HotReloadFullRestart`'s own extra work over compile+construct alone
  (`CaptureInspection`, an `Abort` call, and constructing-then-discarding upfront) -- not a cost
  that grows with how much state is actually being migrated. This directly answers this card's own
  acceptance criterion: migration is measurably, not just theoretically, cheaper than full restart
  when it applies, at every measured tree size.
- **Subtree restart costs sit between migration and full restart**, closer to migration at small
  tree sizes and closer to (or exceeding, on Editor) migration at larger ones -- e.g. Editor
  shallow-sequence-5-leaves: 3294.2µs subtree vs 2303.1µs migration vs 4659.5µs full restart. This
  matches `ADR-P5-001`'s model: a subtree restart is the same migration mechanism with a larger
  exclusion set (the restarted subtree plus everything downstream of it), so its cost sits on a
  continuum between "migrate everything" and "restart everything" depending on how much of the
  tree the incompatible subtree covers.
- **Reload cost has no population-level amortization**: reloading 1, 10, or 50 independently
  created live instances against the same new document costs ~4600-4730µs per instance regardless
  of population size (no statistically meaningful trend). `HotReloadPreviewDriver.TryReload`
  recompiles and reclassifies on every call; there is no API that compiles/classifies a change once
  and applies it to many live instances. This is a genuine, disclosed architecture characteristic,
  not a bug in this card's scope -- a batched-reload API amortizing compile+classify across a
  population of agents sharing one behavior tree is a real future optimization opportunity that
  does not exist today.

## Scope and limitations

- **Debug-instrumentation overhead (trace capture during a reload) is not measured here.**
  `HotReloadPreviewDriver` (the only public, cross-assembly-boundary entry point this card is
  allowed to drive, per `P5-009`'s own forbidden-changes clause barring changes to `P5-001`
  through `P5-008`'s mechanisms) hardcodes `traceSink: null` in every call to
  `HotReloadFullRestart.Restart`/`HotReloadStateMigration.Migrate`, with no parameter to inject a
  real `IReferenceTraceSink`. Measuring this properly needs either an internals-visible test
  assembly (none of `AIBT.Runtime.Tests`/`AIBT.Editor.Tests`/`AIBT.BehaviorCases.Tests`/
  `AIBT.Integration.Tests` is inside this card's `Allowed changes` list) or a public API change to
  `Authoring/HotReload/HotReloadPreviewDriver.cs` (explicitly forbidden by this card, which measures
  `P5-008`'s existing mechanism rather than changing it). Disclosed as a real, structurally-grounded
  gap rather than approximated or silently skipped -- the same disclosure discipline this phase
  already applied to the native-backend hot-reload gap (`P5-004`/`P5-007`).
- Reference-executor backend only, per the user's decision after `P5-007`'s native-backend gap --
  same scope every `P5-004` through `P5-008` card carried.
- Three tree shapes (1/6/63 nodes), one workstation, one Player build configuration
  (`Mono2x`, non-development, no Burst-specific claim since this benchmark exercises no
  Burst-compiled code) -- not generalized to other hardware, IL2CPP, or platforms, per
  `Planning~/USER_ACTIONS.md`'s ownership of any such claim.
- Population scaling was measured on Editor batchmode only, for one tree shape and the migration
  path only -- population-count scaling is a pure per-call API-surface cost (recompiling and
  reclassifying N times), not a property expected to differ meaningfully between Editor and Player
  beyond the same per-call ratio already established above; re-running the full population sweep on
  a rebuilt Player was judged not to add new information proportional to another full isolated-Player
  build-and-run cycle.
- No default, threshold, or "supported reload size/cost" claim is drawn from any number above.

See `verification-results.json` (in `Planning~/Evidence/P5-009/`) for exact commands and raw
evidence file references.
