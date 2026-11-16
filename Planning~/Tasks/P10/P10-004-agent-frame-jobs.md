# P10-004 — Jobs stage J2: one Burst job work item runs one agent's whole update

Status: `Review`

## Objective

Implement the opt-in `JobsAgentFrame` scheduling policy designed in
`Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md`: one Burst `IJobFor` work item advances
one agent through its whole update, including the generated node bodies and the dispatch framing
around them. The main thread keeps the world bridge, due collection, one schedule and one complete
per group, trace drain, effect delivery, status and restart bookkeeping, all in agent order.
Observable results of an accepted tree equal Immediate frame by frame.

## Owner-approved changes (and only these)

1. A new opt-in policy value `SchedulingPolicy.JobsAgentFrame` (public, additive). Defaults do not
   change; the automatic selector never picks it.
2. A generator change: each catalog gains an emitted agent-frame entry (Burst job or function
   pointer, decided by measurement). Consumers regenerate catalogs. `ExpectedPublicAbiV2.txt` stays
   byte-identical; new generated members are internal or outside the ABI v2 surface. Old catalogs
   without the entry fall back with a diagnostic.
3. Fallback: trees that write shared or world scope, contain managed or async leaves, or use
   managed bridges run under the current behavior; the reason is reported by a new additive
   diagnostic. Shared-write visibility semantics unchanged (ADR option 3a).
4. A per-update step limit with a clear agent failure and a new diagnostic, mandatory in the new
   policy, also added to Immediate and BatchedJobsSameFrame with a generous configurable default.
   This is the only approved change to existing behavior.

## Depends on

- P10-003 (cheaper framing, frame-digest equivalence test, ABBA harness).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Documentation~/performance.md`,
  `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md`,
  `Planning~/Evidence/P10-002/README.md`, `Planning~/Evidence/P10-003/README.md`,
  `Spikes~/P10-002-J2/README.md`, `Benchmarks~/Phase10/PlayerPerf/README.md`.

## Order of work

- **Part A — risk gate (before the main implementation).** Prove in an IL2CPP non-development
  Player that a generated node body runs inside a Burst job for a whole agent update; measure µs per
  dispatch inside the job against the 1.6–1.7 µs main-thread figure; decide between an emitted job
  and a function pointer on measured facts; list every dispatch framing step and its blittable path.
  Stop and report if any step cannot run in a job without changing observable results.
- **Part B — implementation.** Step limit first (with tests), then the generator entry and the
  rebuilt `Analyzers/AIBT.CodeGen.dll`, then the runtime policy, fallback diagnostic,
  `NativeTreeAgent` opt-in, and the equivalence/fallback/allocation/lifetime tests.
- **Part C — measurement and docs.** Quick benchmark `-agentframe` variants, interleaved ABBA
  against a Player built from `93e7522`, worker or total CPU time and frame wall time, public API
  diff, static verification, evidence, ADR status, user documentation, changelog.

## Scope

- `Runtime/Integration/` (host, generated dispatch adapter, facade), `Runtime/Scheduling/`
  (scheduler, profiles, policy, new agent-frame driver), `Runtime/Execution/Burst/Dispatch/`
  (native framing reuse), `CodeGen~/AIBT.CodeGen/` and `Analyzers/AIBT.CodeGen.dll`.
- `Tests/Integration/NativeRuntime/` and other test assemblies for the new tests.
- `Benchmarks~/Phase10/PlayerPerf/`, `Documentation~/performance.md`, the ADR, `README.md` (if the
  opt-in is user-facing there), `CHANGELOG.md`, `Planning~/Evidence/P10-004/`.

## Forbidden changes

- Beyond the four approved items: execution semantics of existing policies (tick order, statuses,
  traces, effects and order, diagnostics), scheduler defaults, ABI v2, the package version,
  existing public or low-level APIs (additive changes only).
- Weakening the equivalence comparison. A class of trees that cannot match Immediate exactly is
  excluded by fallback, or the work stops and is reported.
- Pushing, or changing/committing the parent repository outside the submodule.

## Deliverables

- Gate outcome with Player numbers (Part A) in the evidence.
- `JobsAgentFrame` policy, generator entry, fallback diagnostic, step limit, `NativeTreeAgent`
  opt-in; regenerated package catalogs and samples (through the rebuilt analyzer).
- Tests: frame-by-frame equivalence with Immediate (trace, status, effects with order, blackboard
  bytes, run counts) on the existing three trees and on trees with facts, effects, reactive aborts,
  parallel and restarts; every fallback reason; zero managed allocation for warmed-up frames;
  scheduler destroyed mid-frame without leaks; step-limit failure in every covered policy.
- Interleaved IL2CPP numbers (Immediate, BatchedJobsSameFrame, JobsAgentFrame at N = 1000 and
  5000), worker or total CPU time and frame wall time for the new policy.

## Acceptance criteria

- The equivalence test passes for the new policy; existing tests match or exceed the baseline
  counts (`AIBT.Runtime.Tests` 707, `AIBT.Editor.Tests` 635/636 with the known host-only failure,
  `AIBT.Integration.Tests` 82, `AIBT.BehaviorCases.Tests` 65, `AIBT.NativeBurstDispatch.Tests` 79,
  `AIBT.NativeTreeAgentBlackboard.Tests` 33, `AIBT.NativeSharedBlackboard.Tests` 36,
  `AIBT.PlayMode.Tests` 1).
- Public API diff against `93e7522`: additions only. `ExpectedPublicAbiV2.txt` byte-identical.
- No existing single-threaded or Jobs quick case slower by more than 5% in both interleaved
  comparisons.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1, Tools/Unity/Invoke-UnityTests.ps1 (all assemblies above)
Tools~/Verification/Verify-Static.ps1
Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath <93e7522 copy>
AibtPerfTools/Invoke-Perf.ps1 -Step Run -Quick   (IL2CPP, interleaved before/after/after/before)
```

## Handoff notes

- Evidence: `../../Evidence/P10-004/README.md`. Raw data:
  `C:\UnityProjects\_aibt_acceptance\perf-data\P10-004\`.
- Gate: the generated node body runs Burst-compiled inside the job for every agent (0 managed
  agents, 94–96% on workers); 0.48–1.17 µs per dispatch on one thread. Emitted job chosen: a
  function pointer measured the same (−4%…+3%).
- Result: main thread 2.1–3.5× lower than Immediate for small trees and 10.7× for
  `shared-reactive16` (interleaved, IL2CPP, 23 workers); total CPU on one thread 0.46–0.74× of
  Immediate. No existing case more than 5% slower in both comparisons.
- Reachable fallbacks: `ManagedDispatch`, `StepBudgetConfigured`, `CatalogWithoutAgentFrameEntry`;
  `SharedOrWorldScopeWrite` and `AsyncOrManagedLeaf` are defensive (the adapter refuses those
  bindings earlier).
