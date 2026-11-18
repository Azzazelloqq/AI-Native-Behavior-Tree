# P10-013 — Reactive composites that keep their running child (finding F2)

Status: `Review` — stage 1 (specification, manifests, reference executor, behaviour cases) done
2026-11-17; stage 2 (native executor and equivalence) done 2026-11-18, awaiting the owner's
acceptance; stage 3 (IL2CPP) not started. The
ADR was accepted on 2026-11-17 with all nine recommendations of its §6 (decision AIBT-048).

## Objective

Long-running work (external simulation actions, `aibt.stdlib.wait` with `ticks >= 2`, `Timeout`
over a long child) can run under a reactive composite: guards are re-evaluated from child 0 on every
update, but the running child is aborted only when an earlier child takes over or ends the composite,
or when the composite is aborted from above. Existing trees keep their behaviour.

## Decision record

`Documentation~/decisions/ADR-P10-013-non-restarting-reactive-composites.md` (Accepted 2026-11-17). Chosen:
two new built-in composites with new type IDs (`aibt.core.reactive-sequence-retaining`,
`aibt.core.reactive-selector-retaining`), not `typeVersion: 2` — the registry, validator, code
generator and native classifiers support one version per type ID (ADR §3). Open questions: ADR §6.

## Depends on

- P10-011 (finding F2, evidence §7) and P10-012 (benchmark numbers).
- Owner's answers to ADR §6, in particular question 1 (amending the P7-028 statement that
  `aibt.core.` is reserved for the original 11 nodes).

## Required reading

- `Documentation~/specifications/execution-semantics-v1.md`, `reference-executor-machine-v1.md`,
  `node-contract-v1.md`, `compiled-program-v1.md`, `trace-v1.md`.
- `Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md`,
  `ADR-P10-008-simulation-container.md`, this task's ADR.
- `Planning~/Evidence/P10-011/README.md` §5.1 and §7, `Planning~/Evidence/P7-028/README.md`.

## Scope (after approval, by stage; see ADR §4 and §5)

- Stage 1: the specifications above; `Authoring/Model/Nodes/BuiltInNodeManifests.cs`,
  `Runtime/Nodes/Contracts/RuntimeBuiltInCatalogAuthority.cs` (regenerated),
  `Authoring/Validation/TreeValidator.cs`, `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs`,
  `Runtime/Execution/Reference/**`, `Runtime/State/Reference/ReferenceFrame.cs`,
  `Agents/Runtime/ManagedTreeInstance.cs`; reference tests and behaviour cases; fixtures pinning
  registry or content hashes.
- Stage 2: `Runtime/Execution/Native/**` (lifecycle machine, reactive policy, hot reload),
  `Runtime/State/Native/**` (capacity), `Runtime/Simulation/TreeGroup.Debug.cs`; native, equivalence,
  hot-reload and simulation tests.
- Stage 3: `Benchmarks~/Phase10/PlayerPerf/` (new trees `act-retain16`, `io-retain16`), evidence.
- Docs throughout: `Documentation~/generated/*` (regenerated), `simulation-container.md`,
  `performance.md`, `README.md`, `CHANGELOG.md`; `Planning~/Evidence/P10-013/`, this card,
  `work-items.json`.

## Forbidden changes

- v1 reactive behaviour, manifests and handlers; `ExpectedPublicAbiV2.txt` (byte-identical); the
  compiled format version; the trace format; the package version; existing trees, samples, golden
  cases and benchmark trees; pushing; committing the parent repository.

## Deliverables

- ADR stage (done): the ADR, this card, the `work-items.json` entry.
- Stage 1 (done): ADR §5 stage 1; evidence `Planning~/Evidence/P10-013/README.md`.
- Stage 2 (done, in review): ADR §5 stage 2; evidence in the same README, section "stage 2".
- Stage 3: after the owner accepts stage 2.

## Acceptance criteria

Per stage, as listed in ADR §5. In summary:

- v1 pin tests (wait, timeout, cooldown under v1 reactive) exist before production code changes and
  keep passing.
- Reference executor follows ADR §2.1 for worked examples 1–6, budget suspension, nesting, Parallel,
  abort from above, empty composites; observers under the new composites are rejected by validation.
- Native traces equal reference traces on every new behaviour case; Immediate, Jobs and
  JobsAgentFrame frame-by-frame equal; zero managed allocation per tick; hot reload v1↔retaining
  restarts the subtree.
- Simulation: under a retaining selector an action is started once, its completion is consumed and a
  preemption cancels it with `Preempted`; the existing F2 test still pins v1.
- IL2CPP: `act-retain16` has no steady-state `Preempted` cancels and consumes its completions, `Step`
  E/B ≤ 1.2; `io-retain16` not slower than `io-reactive16`; v1 trees unchanged within noise; 0 bytes
  per tick.
- All AIBT test assemblies equal the baseline plus the new tests; `Verify-Static.ps1` passes.

## Required verification

```text
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityTests.ps1 -Assemblies <each AIBT test assembly>
powershell -NoProfile -ExecutionPolicy Bypass -File Tools~/Verification/Verify-Static.ps1
IL2CPP Player benchmark as in Planning~/Evidence/P10-011 §5.1 and P10-012 (stage 3)
```

## Handoff notes

- The ADR stage changed documentation and planning files only; no test was run (none needed).
- `typeVersion: 2`, the plan assumed before this ADR, is not workable without multi-version support
  (ADR §3 (a)).
- The fix does not close the cost gap between `io-reactive16` and `io-loop16`; that gap is guard
  re-evaluation, which the owner's decision keeps (ADR §1.3).
- Stage 1 (2026-11-17): see `Planning~/Evidence/P10-013/README.md` for what changed, the test table,
  the public API delta, deviations from the ADR wording and open issues. The native path rejects the
  new type IDs explicitly (generated compiler `AIBT3012`; native machine build and generated runtime
  definition refuse them); stage 2 replaces that rejection with the native implementation.
  `Agents/Runtime/ManagedTreeInstance.cs` needed no change: the deferred reevaluation request lives
  in the reference machine.
- Stage 2 (2026-11-18): see the "stage 2" sections of `Planning~/Evidence/P10-013/README.md` (spike
  outcome, what changed, test table, public API delta: none, deviations, open issues). The retained
  branch is parked in the suspended-frame region during a guard pass, so the frame array stays
  NodeCount long. Two pre-existing v1 native defects were found and left unchanged (findings F-S2-1:
  Parallel restore with no free frame overwrites a lower suspended branch; F-S2-2:
  `NativeHotReloadFullRestart.TryRestart` refuses a v1 reactive composite with a running branch);
  both are pinned by ignored tests. `RetainingReactiveNativeRejectionTests` became
  `RetainingReactiveNativeAcceptanceTests`.
