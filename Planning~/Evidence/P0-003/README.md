# P0-003 Web backend spike evidence

Observed on 2026-08-13 with Unity `6000.5.8f1`, Chrome `151.0.7922.137`, and Firefox `153.0.4` on Windows 11 x64.

## Result

- A non-development IL2CPP WebGL Player build completed successfully.
- Chrome and Firefox each passed all five accepted P1-018 behavior cases with zero failures and zero input diagnostics.
- Both browsers passed full normalized semantic equivalence between `SingleThreadImmediate` and `SingleThreadBudgeted(1)`. The reactive scenario exercises a nonempty blackboard, observer-driven abort/source trace, and terminal cleanup; the async scenario exercises a nonempty command with operation identity and payload.
- Equivalence covers final progress/root, total semantic steps, active counts, all final blackboard values and versions, ordered commands, diagnostics, and all semantic trace fields. Only tree-instance/sequence identities are normalized and only `BudgetYielded`/`ExecutionResumed` are filtered.
- The Player self-reported one logical processor, matching Unity Web's tested single-thread execution path.
- The managed reference executor cannot provide a representative `IJob.Run` or direct Burst entry point. Those variants are unsupported by this spike and are deferred until a native packed executor exists.

## Decision

The Web backend should expose `SingleThreadImmediate` and `SingleThreadBudgeted` over the same semantic contracts. The budgeted policy is the frame-cooperative entry point; immediate execution remains useful when the caller explicitly accepts completing the update in one call. This spike does not define an automatic switching threshold.

Do not expose or claim Web support for job-scheduled, batched, pipelined, or Burst-direct execution of the current managed reference executor. Re-run the spike against the native packed executor before adding such a policy.

## Scope and limitations

- Functional compatibility is evidenced only for the exact desktop browser and engine versions above on this machine. It is not a declaration of minimum browser versions.
- Safari is unverified because no macOS host was available. Mobile browsers are unverified and unsupported by this evidence.
- Headless Chrome used SwiftShader while Firefox reported an ANGLE Intel D3D11 route. The one-run throughput observations are not suitable for browser comparison or stable performance baselines.
- The Player reported no generation-0 collection during the measured windows, but nonzero coarse managed-heap deltas. `GC.GetTotalMemory` is not an allocation profiler, so zero allocation is not proven.
- The Web build contains Burst package artifacts, but the tested AIBT executor path is managed and was not Burst-compiled.

Machine-readable build, browser, source-fingerprint, and artifact-hash evidence is in `verification-results.json`. Pilot performance observations are recorded under `Benchmarks~/Platform/Web/`.
