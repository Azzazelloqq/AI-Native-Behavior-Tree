# P10-014 evidence: fixes from the P10-013 code review

Base: `feature/aibt-consumer-workflow` at `3e4b09e` (P10-013 stages 1–3). Review report: session notes
`P10-013-code-review.md` (independent review of `1dd7dd0..3e4b09e`). Editor 6000.6.9f1, tests run with
`Tools/Unity/Invoke-UnityTests.ps1` (async), touched classes only; the full run of all assemblies is
done by the orchestrator at acceptance.

Part C (the IL2CPP slowdown of v1 loop trees) was **removed by the owner on 2026-11-18**: no Player
builds and no measurements were made in this task.

## Findings

| Finding | Reproduced | Failing output before the fix | Fix | Tests |
|---|---|---|---|---|
| D1: a Parallel's branch abort overwrites the outer abort target | Yes, 12 of 16 cases failed (faults `NativeLifetimeStateInvalid`, wrong results, branches never aborted) | `d1-before-fix.txt` | The outer abort (target, reason, source, pending flags) is saved on the Parallel's first branch record when its branch aborts begin and restored when they end, as the reference keeps `ParallelAbortResume*` per Parallel. A tree stop or hot reload that replaces a pending abort rewrites the saved records so the stop wins all the way up. | `NativeParallelOuterAbortTargetTests` (17) |
| D2: restoring a suspended segment shifts frames in the wrong direction when the segment is not at the region start | Yes, 3 of 3 v1 trees: a leaf ticked twice, branches skipped | `d2-before-fix.txt` | `_safeSegmentRestore` removed; every program restores with one in-place rotation (`MoveSegmentToStackTop`, three reverses + copy down). An exhaustive test over all layouts up to 10 frames shows the rotation produces the same active and suspended frames as the old copy-then-shift wherever the old one was correct. | `NativeParallelSegmentRestoreTests` (4); the existing Parallel suites unchanged |
| D3: `RetainVisitRestPending` survives an abort requested in the window after a Parallel guard's takeover | Yes, 3 of 3 (flag still 1 after a tree stop; an observer reset faulted) | `d3-before-fix.txt` | `TryRequestAbort` and `TryApplyObserverTransition` clear the flag: the abort replaces the rest of the visit. | `RetainingVisitRestAbortTests` (3) |
| F-S2-2: full hot-reload restart of a v1 reactive composite with a running branch was refused | Yes (the existing `[Ignore]`d test) | the ignored test's reason | `NativeHotReloadFullRestart` requests the `HotReload` abort with `replacePendingForTreeStop: true`; `TryRequestAbort` accepts that flag for `HotReload` as well as `TreeStopped`. | `RetainingReactiveHotReloadTests` (7) |
| R1: state migration while a retaining guard pass holds parked frames | Hardened. Migration may run inside an open update (pinned by `NativeHotReloadStateMigrationTests.Reorder_AfterTerminalTick_PreservesPendingExitReason`), but it copies only the active frames and `Depth`, so a parked retained branch, a held guard branch or a pending visit rest would be dropped without Abort/Exit | — | `NativeHotReloadStateMigration.TryMigrate` refuses with `NativeLifetimeStateInvalid` exactly when `Control.RetainOwnerDepth != 0 \|\| RetainPhase != 0 \|\| RetainVisitRestPending != 0`; the old instance runs on. Open updates and active dispatches are not refused (no guard existed on `3e4b09e`; the first version of this fix refused `UpdateOpen != 0 \|\| ActiveDispatchId != 0` and broke the pinned test, corrected 2026-11-19). | `RetainingReactiveHotReloadTests.AMigrationDuringAnOpenUpdateWithAParkedRetainedBranch_IsRefused_AndTheOldInstanceRunsOn`, boundary `AMigrationDuringAnOpenUpdateWithNoRetainingPass_IsAllowed_AndTheGuardPassesResume`; `NativeHotReloadStateMigrationTests` unchanged |
| R2: no guard pass in the first update after a structural reset | Yes, but shared with the reference (its requested-reevaluation path returns before `PrepareUpdateStart`) | — | Not changed (reference behaviour is fixed by the task). Documented in the ADR hot-reload row as non-normative. | — |

## v1 behaviour changes (native only; each now matches the reference executor)

- A `Timeout` above a Parallel that reaches its deadline now aborts every running branch and
  returns the timeout's configured result. Before, the branch abort overwrote the timeout's target:
  branches were left running or the machine faulted.
- A v1 reactive composite above a Parallel no longer faults when its condition changes; it aborts
  every branch and restarts the owner.
- A nested Parallel whose terminal decision is made while its sibling branches are suspended now
  aborts those branches and returns its result to the outer Parallel.
- An observer reset whose owner is above a Parallel no longer faults.
- In small v1 Parallel trees whose frames fill the arena, no leaf is ticked twice in one update and
  no branch is skipped any more (D2).
- After a Parallel guard's takeover, a tree stop or observer reset in the window before the rest of
  the guard's visit no longer leaves that visit pending for the next update (D3).
- A full hot-reload restart of a v1 reactive composite with a running branch now succeeds (F-S2-2):
  the old instance is aborted and the fresh one runs from the root.
- State migration is refused (old instance kept) only while a retaining composite's guard pass or
  takeover is in progress (R1); before P10-013 that state did not exist, so no earlier migration is
  refused.

## Open questions for the owner

1. **A tree stop that replaces an abort already in progress inside a Parallel.** The reference
   `RequestAbort` in that state loses the stop: it finishes the abort it was doing and the owner
   then completes normally (for example `T timeout F; X timeout`). Native lets the stop win: every
   frame up to the root is aborted with `TreeStopped`. The specifications say a tree stop aborts the
   whole active path, so native was kept and the four `StopsReplacingAPendingAbort` cases assert the
   native records explicitly. The reference was not changed (forbidden). Stops requested at update
   start, with no abort pending, agree with the reference and are compared with it (3 cases).
2. **R2 wording.** Proposed sentence for `execution-semantics-v1.md` near line 169 (the hot-reload
   bullet): "In the first update after a structural edit that resets a composite's cursor, the
   reset replaces update-start processing, so no retaining composite on the active path has a guard
   pass in that update." Not applied: the spec's meaning is not changed in this task.
3. **State migration and suspended Parallel branches (pre-existing, not changed).** A v1 Parallel
   keeps its resting branches in the suspended-frame region between updates too, and
   `TryMigrate` copies only the active frames, `Depth` and no `ParallelBranches` records, as on
   `3e4b09e`. Whether migration of a Parallel with running branches loses them was not tested here;
   the R1 guard deliberately does not refuse it, since that would change existing migrations. A
   follow-up should check it.
4. **Parallel step counts** (review point 4), pinned not aligned:
   `RetainingReactiveNativeCoverageTests.ParallelStepCountsDifferExactlyByLeafRestsAndParallelCompletions`.
   Native = reference + one step per branch that rests on a leaf that ticked Running (the
   `ParallelBranchSuspended` step) + one step per Parallel that completes (native accepts the last
   branch's result and exits in two steps, the reference in one; seen step by step on 2026-11-19).
   Records are identical. Native therefore hits `MaximumStepsPerUpdate`/`MaximumStepsPerThink`
   slightly earlier in Parallel trees.

## Part B

- Native coverage (`RetainingReactiveNativeCoverageTests`, compared with the reference record for
  record, both drive modes): guard takeover in the first and in the second branch of a Parallel;
  Cooldown as the retained child and as a guard (2 cases); Repeater as the retained child and as a
  guard (2 cases); the step-count pin above.
- A step limit inside a guard pass in the simulation container:
  `TreeSimulationActionTests.AStepLimitInsideAGuardPass_FaultsTheAgentCleanly_AndARestartRunsItAgain`
  (both job modes).
- Docs: `Documentation~/simulation-container.md` pitfall corrected (stepA is re-entered from
  scratch, takes over and aborts stepB with `Preempted`); ADR-P10-013 status lines; a note that the
  native trace writes no abort source for a takeover (§2.3); the hot-reload row (R1, R2).

## Existing test expectations changed

No existing expectation was weakened, and no golden file changed.

- `RetainingReactiveNativeEquivalenceTests.V1ParallelWithTwoSuspendedBranchesMatchesTheReference`:
  `[Ignore]` removed (it encoded D2 as a known failure); its assertions are unchanged.
- `RetainingReactiveHotReloadTests.AV1ReactiveCompositeWithARunningBranch_CanBeFullyRestarted`:
  `[Ignore]` removed (F-S2-2) and strengthened: it now also asserts that the old instance was
  aborted and that the fresh instance runs from the root.
- `RetainingNativeTwin` (test helper): exposes `ParallelBranches` for the new no-leak assertions.

## Static checks (2026-11-19)

- `Tools~/Verification/Verify-Static.ps1`: passed (JSON schemas 7, work items 178).
- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`: unchanged (`git diff` empty).

## Test runs after all fixes (touched classes only, all passed)

| Filter | Result |
|---|---|
| `NativeParallelOuterAbortTargetTests` | 17/17 |
| `NativeParallelSegmentRestoreTests` | 4/4 |
| `RetainingVisitRestAbortTests` | 3/3 |
| `RetainingReactiveHotReloadTests` | 7/7, then 8/8 after the R1 correction (2026-11-19) |
| `NativeHotReloadStateMigrationTests` (after the R1 correction) | 17/17 |
| `RetainingReactiveNativeCoverageTests` | 9/9 |
| `RetainingReactiveNativeEquivalenceTests` | 31/31 |
| `NativeMatchesTheReferenceRecordForRecord` (parameterised equivalence cases) | 39/39 |
| `AnAbortDuringAGuardPassAbortsTheGuardBranch` | 5/5 |
| `NativeParallelAndDecoratorPolicyTests` | 19/19 |
| `AStepLimitInsideAGuardPass` (Integration, both job modes) | 2/2 |

Not run here: the full EditMode/PlayMode suites of all assemblies (left to the orchestrator at
acceptance), Player builds (part C removed).
