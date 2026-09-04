# P7-031 verification

Status: **Done**, 2026-09-04. The owner approved the initial protocol, subsequent recipe repairs
and commits. Earlier review fixes were committed in 1ff1b2a; this evidence covers the completed
follow-up. Unrelated host-project changes are preserved. No push.

## Implemented behavior

- Assets-relative canonical destination validation before mutation, including rooted/drive-relative
  paths, escapes, ambiguous components, existing destinations and link/reparse ancestry.
- One Library-backed attemptId bound to staged relative paths and contents. The main-thread hook
  imports and explicitly requests compilation after any current compile. Both staging assemblies
  must be verified by compiler events. assemblyCompilationNotRequired is up-to-date evidence for
  that attempt; any rebuilt assembly requires reload before success is exposed.
- Actual Editor log diagnostics only; no success from raw log markers. Unknown, superseded,
  restarted or changed-content attempts are rejected. Server and recipes use attemptId.
- Background TCP test dispatch owns Persistent native storage and disposes it after the request.
  Successful apply removes the staging-only catalog source after validating its ancestry, preserving
  the existing assembly layout. Rejected requests retain staging content.

## Verification

- Focused EditMode: **46/46 passed**, job 1acf994e9aaa42c79c88bbcd86ab2c90.
- Full host EditMode with P7-032: **1726/1729 passed, 3 failed, 0 skipped**, job
  b38994b8ba4e472087dbfbde0834563b. The same two CodeGen PackageInfo assertions and LocalSaveSystem
  autosave assertion fail as before. Exact names/messages/counts are in verification-results.json.
- Real Windows filesystem probes against the production C# code: **16/16**, including destination,
  staging-root and companion-catalog junction rejection without source/target mutation.
- Server build: 0 errors/warnings. Static checks: 7 schemas and 137 work items passed.
- Full generated documentation regenerated in Unity. git diff --check passed.
- The first regressions reproduced both recipe blockers. The worker-thread fixture was corrected
  to use a real single-node catalog with the supported typed binding/configuration; generic
  multi-node and empty-configuration fixtures were not substituted for the staging contract.
  The final fixture executes the generated Enter/Tick callbacks on a real managed worker thread.

## Live TCP recipe

The final run used an initially absent staging slot and a disposable UInt32 condition. All calls
went through the actual background TCP bridge: generate, preview, scaffold, start/check, test, apply.
See live-final.json for responses. test returned valid=true, dispatchProven=true, Enter success,
Tick Success and callback Success. Apply moved node/test/asmdef under Assets/P731-LiveVerification.
Without manual intervention, the subsequent Unity compilation had zero errors, loaded the applied
shard and found zero remaining staged .cs files. All owned probe sources were then removed.

Earlier negative/ordering probes in live-verification.json remain historical evidence: completed
Auto Refresh before start, import/domain reload, repeated checks, intentional CS1029 compilation
failure and recovery, changed-content/stale-hash AIBT9032, and absent-registry AIBT9035. Its earlier
test/apply failures are fixed by this follow-up; they are not the final recipe outcome.

No Player build or new performance claim is made for this Editor-only workflow. The generated
paired NUnit scaffold remains explicitly inconclusive; real native dispatch proof comes from
test_node and the committed regression fixture. No general task queue or transport rewrite.
