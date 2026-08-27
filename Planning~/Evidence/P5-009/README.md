# P5-009 hot-reload benchmark evidence

## Result

- `Benchmarks~/Phase5/HotReload/` (new): a shared measurement harness
  (`Unity/Runtime/HotReloadBenchmarkRunner.cs`) drives `P5-008`'s public `HotReloadPreviewDriver`
  facade to isolate compile-only, full-restart (`P5-004`), compatible-migration
  (`P5-005`/`P5-006`), and subtree-restart costs at three tree sizes (1/6/63 nodes), 5 warmup + 15
  measured samples each. The exact same source ran unmodified in Editor batchmode and, copied into
  an isolated project via `Run-HotReloadPlayerBenchmark.ps1`, in a real non-development Windows x64
  Standalone Player -- satisfying this card's explicit "at least one measurement runs on a real,
  non-Editor Player build" acceptance criterion.
- **Migration is measurably cheaper than full restart, not just cheaper by design**: full restart
  costs ~1.9-2x a compatible migration at the same tree size, consistently on both Editor and
  Player. Full analysis, tables, and every other finding (Editor-vs-Player gap, subtree-restart
  cost sitting between the other two, population-scaling having no amortization) in
  `Benchmarks~/Phase5/HotReload/README.md`.
- A locale bug was found and fixed along the way: `double.ToString("F3")` used the current culture
  (comma decimal separator on this machine), producing invalid JSON. Fixed with
  `CultureInfo.InvariantCulture`; verified by re-running and validating the output with a JSON
  parser.
- A build-reference bug was found and fixed: the isolated Player project's initial `manifest.json`
  omitted `com.unity.burst`/`com.unity.collections`, which `AIBT.Runtime` unconditionally references
  regardless of what this specific benchmark exercises -- the build failed with `NativeList<>`/
  `FixedStringNBytes` resolution errors until those packages were added back (mirroring `P4-008`'s
  own manifest). A missing `using UnityEditor.Build.Reporting;` in the build driver was also found
  and fixed the same way (a real `CS0103: BuildResult does not exist` compiler error, not a
  hypothetical).

## Decision

- **No new mechanism was built or changed.** This card only measures `P5-004` through `P5-008`'s
  already-decided/accepted mechanisms, per its own forbidden-changes clause.
- **Debug-instrumentation overhead is disclosed as out of scope, not approximated.**
  `HotReloadPreviewDriver` (the only entry point this card may drive) hardcodes `traceSink: null`
  with no injection point; measuring the real cost would require either an internals-visible test
  assembly (none of the four `InternalsVisibleTo` grants are inside this card's `Allowed changes`
  list) or a public API change to `Authoring/HotReload/HotReloadPreviewDriver.cs` (explicitly
  forbidden -- this card measures `P5-008`'s mechanism, it does not change it). This mirrors the
  same disclosure discipline `P5-004`/`P5-007` already applied to the native-backend hot-reload gap
  -- a real, structurally-grounded scope boundary, not an oversight.
- **Population scaling was added as a supplementary measurement** to satisfy the card's own
  Deliverables language ("multiple tree sizes and multiple agent-population sizes"), scoped to
  Editor batchmode only, one tree shape, one strategy (compatible migration) -- population-count
  scaling is a pure per-call API cost (recompile+reclassify N times, no shared state), not expected
  to differ meaningfully between Editor and Player beyond the same per-call ratio already
  established by the main sweep, so a second full isolated-Player build-and-run cycle was judged
  not worth the additional cost for this secondary dimension.
- **A dedicated Windows Player build+run pipeline was written** (`HotReloadBenchmarkBuild.cs`,
  `Run-HotReloadPlayerBenchmark.ps1`), mirroring `P4-008`'s general pattern (isolated project,
  `BuildPipeline.BuildPlayer`, structured build evidence, a
  `[RuntimeInitializeOnLoadMethod]`-gated Player probe, verified success markers) but using the
  default scripting backend (Mono2x) rather than IL2CPP/Burst, since this benchmark exercises no
  Burst-compiled code and makes no Burst-specific claim.

## Scope and limitations

- Debug-instrumentation overhead (trace capture during a reload) is not measured -- see Decision
  above and `Benchmarks~/Phase5/HotReload/README.md`'s "Scope and limitations" section.
- Reference-executor backend only, per the user's decision after `P5-007`'s native-backend gap.
- Three tree shapes, one workstation, one Player configuration (Mono2x, non-development); not
  generalized to other hardware or scripting backends.
- No default, threshold, or "supported reload cost/size" claim is drawn from any number here, per
  this card's own forbidden-changes clause.

See `verification-results.json` for exact commands and results.
