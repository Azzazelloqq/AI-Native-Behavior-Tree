# P10-007 — Automatic selection of JobsAgentFrame from 64 consecutive eligible agents

Status: `Review`

## Objective

Default-profile populations get the JobsAgentFrame speed-up measured in P10-005 without opting in,
and nobody pays its fixed cost below the crossover. Two steps, one commit each:

1. **Short batches run in place.** Inside the JobsAgentFrame path, a batch of consecutive
   job-eligible members shorter than a threshold (default 64, a tunable) runs on the main thread
   exactly as `Immediate` does; no job is scheduled.
2. **Selector rule.** Hosts whose profile does not force a policy run as JobsAgentFrame when they
   are part of a contiguous run of at least 64 job-eligible members in the due order (same profile
   and catalog); otherwise nothing changes. Ineligible members stay on their current policy without
   a warning. An additive switch turns the automatic selection off.

## Owner decisions for this card

- "Автовыбор с порога 64" — automatic selection with threshold 64, in two steps (option (b) of
  `Planning~/Evidence/P10-005/README.md` section 10.2). This is an explicit approval to change the
  scheduler's default behaviour for this purpose only.
- Measurement kept short: IL2CPP Player, interleaved ABBA, default profile before vs after,
  `shared-loop16` and `shared-reactive16`, N = 16, 63, 64, 256, 2 000.

## Depends on

- P10-005 (measurement and proposal), P10-006 (Unity 6000.6.9f1 baseline).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Planning~/Evidence/P10-004/README.md`,
  `Planning~/Evidence/P10-005/README.md` (sections 5, 6, 10), `Documentation~/performance.md`.

## Scope

- `Runtime/Scheduling/Production/ProductionTreeScheduler.cs`, `SchedulerFrameSnapshot.cs`,
  `SchedulingPolicy.cs` (documentation comments), `SchedulingProfile.cs` (documentation comments).
- Tests in `Tests/Integration/NativeRuntime/` (existing integration assembly) and test updates that
  pin the old threshold where a test covers the job driver itself.
- `Benchmarks~/Phase10/PlayerPerf/` (the P10-007 scenario set), `Documentation~/performance.md`,
  `Documentation~/generated/api-reference-runtime.md` (regenerated), `CHANGELOG.md`,
  `Planning~/Evidence/P10-007/`, this card, `work-items.json`.

## Forbidden changes

- Execution semantics (results, traces, effects and their order), ABI v2
  (`ExpectedPublicAbiV2.txt`), the package version, existing public or low-level APIs (additions only).
- The behaviour of profiles that force a policy (other than step 1 inside forced JobsAgentFrame),
  of `Budgeted` and of hosts with a step budget.
- Tuning the threshold from the measurement; a contradicting number is reported instead.
- Pushing; committing the parent repository.

## Acceptance criteria

- Frame-by-frame equivalence of automatic selection and of in-place batches with Immediate (digest
  approach of `JobsPolicyEquivalenceTests`).
- 63 consecutive eligible members → not selected, 64 → selected, visible in frame entries.
- Interleaved ineligible members break runs (40 + 1 + 40 → nothing selected; 64 + 1 + 10 → only
  the first run).
- Catalogs without the agent-frame entry produce no warning under automatic selection.
- Budget admission with `MaximumBudgetShare` follows the existing rules.
- Automatic selection off restores the previous behaviour exactly.
- All eight AIBT test assemblies match the 6000.6.9f1 baseline plus the new tests; the ABI v2 test
  and the public-API additivity check pass.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityTests.ps1 -Assemblies <each AIBT test assembly>
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
Assets/AIBT/Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath <P10-006 API dump>
Benchmarks~/Phase10/PlayerPerf/Tools/Invoke-Perf.ps1 (IL2CPP Player, ABBA)
```
