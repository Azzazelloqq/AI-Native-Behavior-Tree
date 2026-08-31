# ADR P6-020: Hot-reload debug-instrumentation trace injection

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-028

## Context

`P5-009` measured hot-reload cost (compile-only, full-restart, compatible-migration, subtree-restart)
but could not measure the added cost of running with trace instrumentation active during a reload,
because `HotReloadPreviewDriver.cs`'s own `TryReload` hardcodes `traceSink: null` in its call to
`HotReloadStateMigration.Migrate` (line 104), with no injection point. `P5-009` named two candidate
fixes -- an `InternalsVisibleTo` grant letting a benchmark-owning assembly pass its own sink, or a
public constructor/method parameter on `HotReloadPreviewDriver` -- without picking between them.

## Spike evidence (`Spikes~/HotReloadTraceInjection/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikeHotReloadTraceInjection`, run live via Unity MCP `run_tests`) copied
`HotReloadPreviewDriver`'s own real construction (unmodified in shape, mirroring `P6-013`'s own
spike-facade technique) and called the real, unmodified `HotReloadStateMigration.Migrate` directly
with a caller-supplied `IReferenceTraceSink` (a spike-local sink of the exact same shape
`Authoring/BehaviorCases/AuthoringBehaviorCaseExecutorFactory.cs`'s own already-accepted
`CollectingTraceSink` already uses) in place of the hardcoded `null`.

1. **Compatible migration on an idle instance.** `Migrate` took the real migration path
   (`report.FellBackToFullRestart == false`). **Passed.**
2. **Reload of an active instance.** `Migrate` took its own internal fallback to
   `HotReloadFullRestart.Restart` (`report.FellBackToFullRestart == true`). **Passed.**
3. **Real, disclosed finding: the sink attaches to the resulting fresh machine's future ticks, not
   the migration/restart procedure itself.** Both scenarios produced zero records from `Migrate`/
   `Restart` alone -- confirmed by first asserting `sink.Records.Count` immediately after the call
   (0 in both cases) before discovering the cause. Reading `HotReloadStateMigration.cs` confirmed
   why: `Migrate` and `Restart` thread `traceSink` straight into the *fresh* machine's own
   `new ReferenceExecutionMachine(newProgram, ..., traceSink, ...)` constructor call -- it is wiring
   for that machine's own subsequent execution, not instrumentation of the reload procedure's own
   internal bookkeeping (state capture/copy). One `Update()` tick on the returned fresh machine was
   enough to observe real, non-zero records in both scenarios. **Confirmed by construction, not
   assumed** -- this materially changes what a future benchmark measures (see Consequences).
4. **One injection point, not two.** Because `Migrate` owns the idle-vs-active branch internally and
   forwards the identical `traceSink` to `HotReloadFullRestart.Restart` when it falls back,
   `HotReloadPreviewDriver.TryReload`'s single call into `Migrate` is the only production call site
   this decision needs to change -- confirmed by scenario 2 above exercising the fallback path
   through that one call and still observing real records.

Full raw output is in `Planning~/Evidence/P6-020/README.md`.

## Decision

1. **Mechanism: a new, purely additive `internal` overload, not a public facade change.**
   `HotReloadPreviewDriver.TryReload` gains an internal overload accepting an optional
   `IReferenceTraceSink traceSink = null`; the existing public overload forwards `null` unchanged
   (fully backward compatible, zero public API diff). `IReferenceTraceSink` is itself `internal` to
   `AIBT.Runtime`, so a parameter of this type can only ever be used by internals-visible callers
   regardless of the enclosing method's own accessibility -- there is no practical "public parameter,
   internal type" option to weigh here, which resolves `P5-009`'s two-candidate question directly:
   the `InternalsVisibleTo` route is not merely preferred, it is the only one that is not purely
   cosmetic.
2. **A future benchmark-owning assembly needs two `InternalsVisibleTo` grants, both additive.**
   `AIBT.Runtime` already grants `InternalsVisibleTo` to several test assemblies (`AIBT.Runtime.Tests`,
   `AIBT.Editor.Tests`, etc.) but not to a not-yet-created benchmark project; that project's own name
   needs adding to `Runtime/AssemblyInfo.cs` (to construct/read `IReferenceTraceSink`/
   `ReferenceTraceRecord` directly, mirroring `AuthoringBehaviorCaseExecutorFactory.cs`'s own
   `CollectingTraceSink`) and to `Authoring/AssemblyInfo.cs` (to call the new internal overload on
   `HotReloadPreviewDriver`) -- exactly the technique `P4-001`'s evidence already used ("renamed to
   `AIBT.Runtime.Tests`, matching `Runtime/AssemblyInfo.cs`'s existing `InternalsVisibleTo` grant").
3. **Scope: `TryReload` only, matching the card's own named gap.** `HotReloadPreviewDriver.CreateMachine`
   (used by `TryCreate` and internally to build the very first machine) has its own separate
   hardcoded `null` for the same constructor parameter -- but that machine's sink would capture live
   ticking, not reload cost, a different concern this card was not asked to solve and does not
   bundle in, per its own Forbidden-changes clause against unrelated facade widening. A future card
   wanting initial-construction tracing too can apply the identical additive-overload pattern to
   `TryCreate` separately.

## Consequences

- A future implementation card adds the internal `TryReload` overload plus the two
  `InternalsVisibleTo` grants (naming its own new benchmark assembly), then re-runs `P5-009`'s own
  methodology with a real sink attached to measure genuine per-tick instrumentation cost on the
  post-reload instance.
- That future benchmark must measure **post-reload ticking cost with a sink attached**, not
  "the reload procedure's own cost with a sink present" -- finding 3 above shows the reload
  procedure itself never calls the sink. If measuring the reload procedure's own overhead from
  merely carrying a non-null sink reference through its call chain is also desired, that is a
  distinct, smaller measurement (parameter-passing/branch overhead only, no actual `Record` calls)
  the future card should state separately, not conflate with per-tick instrumentation cost.
- `HotReloadPreviewDriver.CreateMachine`'s own hardcoded `null` (initial construction, not reload)
  remains unaddressed and is not claimed to be covered by this decision.

## Explicitly unverified (stated, not generalized)

- Only `Migrate`'s two branches (compatible migration, fallback to full restart) were spiked;
  `HotReloadStateMigration.cs`'s `IncompatibleRestart`/subtree-exclusion path was not separately
  re-verified for sink propagation, though it follows the identical `new ReferenceExecutionMachine(...,
  traceSink, ...)` construction already read in full.
- The actual per-tick overhead of having a real (non-`Null`) sink attached was not measured here --
  this card decides the injection mechanism only; measuring the cost is the future benchmark card's
  own job, per `P5-009`'s original scope.
