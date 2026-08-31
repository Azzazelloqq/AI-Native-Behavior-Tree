# P6-020 Hot-reload debug-instrumentation trace injection decision evidence

## Result

Done, accepted. `ADR-P6-020` (`AIBT-028`) decides `HotReloadPreviewDriver.TryReload` gains a purely
additive `internal` overload accepting an optional `IReferenceTraceSink`, resolving `P5-009`'s own
two-candidate question directly: since `IReferenceTraceSink` is itself `internal` to `AIBT.Runtime`,
a public-parameter option was never actually available, only cosmetic. A future benchmark-owning
assembly needs `InternalsVisibleTo` grants from both `AIBT.Runtime` and `AIBT.Authoring`, mirroring
`P4-001`'s own already-established technique.

## Real finding: the sink attaches to future ticks, not the reload procedure itself

The spike's first pass asserted the sink captured records immediately after `Migrate`/`Restart`
returned and got zero in both the compatible-migration and full-restart-fallback scenarios.
Re-reading `HotReloadStateMigration.cs` explained why: both paths thread `traceSink` into the
*fresh* machine's own constructor call, wiring it for that machine's future execution -- not
instrumenting the reload procedure's own internal state-capture/copy bookkeeping, which never calls
`Record` at all. One `Update()` tick on the returned fresh machine was enough to observe real,
non-zero records in both scenarios. This is a real, disclosed nuance for the future benchmark card:
"instrumentation overhead during hot reload" means overhead on the *post-reload instance's ticking*,
not the reload procedure's own cost of merely carrying a non-null sink reference through its call
chain -- the two are different measurements, not the same one under two names.

## Verification

```text
Disposable spike (SpikeHotReloadTraceInjection, Tests/Editor/HotReloadTraceInjectionSpike/ during
  this session, archived afterward): 2/2 tests passing, live via Unity MCP run_tests --
  Reload_CompatibleMigration_OnAnIdleInstance_CapturesRealTraceRecords,
  Reload_OfAnActiveInstance_FallsBackToFullRestart_AndTheSameSinkStillCapturesRealRecords
Regression (required by this card's own acceptance criteria, unmodified, live via Unity MCP):
  AIBT.Tests.Editor.HotReload.Preview.HotReloadPreviewDriverTests -- 5/5 passing
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`Authoring/HotReload/HotReloadPreviewDriver.cs`, `Runtime/HotReload/`) was
touched, per this card's own Forbidden-changes clause -- the spike lived temporarily in
`Tests/Editor/HotReloadTraceInjectionSpike/`, then archived to `Spikes~/HotReloadTraceInjection/`
and deleted from `Tests/`, mirroring `P6-013`'s/`P6-015`'s own precedent exactly.

## Handoff

A future, not-yet-numbered implementation card adds the internal `TryReload` overload plus the two
`InternalsVisibleTo` grants (naming its own new benchmark assembly), then re-runs `P5-009`'s own
methodology with a real sink attached to the post-reload instance to finally measure genuine
per-tick instrumentation cost, closing that card's own disclosed measurement gap.
