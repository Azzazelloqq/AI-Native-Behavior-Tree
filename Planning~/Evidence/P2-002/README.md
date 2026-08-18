# P2-002 remediated candidate evidence

Status: accepted. The first independent review returned `Changes required`; all seven findings were remediated and independently accepted in the P2-002 round-2 review on 2026-08-14.

## Outcome

The candidate defines the v1 native ownership, allocator, lifecycle/lease, capacity, overflow, publication, safety, cleanup, and diagnostic contracts. The isolated Unity spike now covers exact program-generation binding, opaque owner/generation/lease tokens, atomic partial-initialization rollback, distinct abort/fault cleanup, exhaustive capacity fields, Shared stream ordering, schedule rollback, and an executed non-development Player ownership path.

Owned deliverables:

- `Documentation~/specifications/native-runtime-v1.md`
- `Documentation~/decisions/ADR-P2-002-native-ownership-and-capacity.md`
- `Spikes~/NativeOwnership/`
- this evidence directory

No production native container or executor file was changed.

## Observable behavior verified

- a pass validates the instance's exact program owner/generation before acquiring leases, invoking callbacks, or scheduling;
- owner tokens contain owner ID, generation, and lease ID; default, stale, foreign, and wrong-generation tokens deterministically return `AIBT4311` even when another live lease exists;
- operations conflicting with a valid live scheduled lease deterministically return `AIBT4312`;
- successful Burst execution commits staged state and publishes the whole command output;
- expected abort and fault are separate terminal states; both finish dependencies, discard staging, release leases, and clean up without committing;
- an injected `Job.Schedule` exception rolls every acquired lease back in reverse order and leaves all owners/pass reusable;
- program, input, instance, and every pass allocation point clean up partial `NativeArray` initialization without publishing an owner or leaking;
- every normative program/config/instance/input/completion/command/Shared/diagnostic/trace record/payload, alignment, work, and scratch limit has a behavioral rejection test;
- arithmetic/representation/counter overflow converts to `AIBT4303` rather than escaping `OverflowException`;
- Shared contribution reservations require strict `TreeInstanceId` order and reject the whole preflight before callbacks/publication;
- Unity collection safety checks reject direct `NativeArray` disposal while a scheduled job owns the view;
- Unity native leak detection ran in `EnabledWithStackTrace` mode and logs contain no native leak marker;
- a non-development Windows Player Burst-AOT compiled and executed the same create/schedule/complete/commit/dispose ownership scenario, emitted the post-cleanup success marker, and exited zero;
- release build and Player logs contain no missing-script/compiler warning, Burst error, or managed fallback marker.

## Verification

Final run on 2026-08-14:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Spikes~\NativeOwnership\Build-And-Verify.ps1
```

Result: pass; Unity `6000.5.8f1`, Burst `1.8.29`, 69 focused EditMode cases passed, zero failed/skipped, non-development Windows Player build passed, launched Player emitted `AIBT_NATIVE_OWNERSHIP_PLAYER_OK`, and exited zero.

```powershell
git diff --check -- Documentation~/specifications/native-runtime-v1.md Documentation~/decisions/ADR-P2-002-native-ownership-and-capacity.md Spikes~/NativeOwnership Planning~/Evidence/P2-002
```

Result: pass. The files are untracked candidate work in the shared tree, so the command verifies tracked diff whitespace only; an additional trailing-whitespace scan over owned text files returned no findings.

Raw Unity logs, XML, Library state, and Player output are generated under ignored `Spikes~/NativeOwnership/artifacts/` and harness cache directories. Stable result facts are recorded in `verification-results.json`.

## Scope and handoff

- Task ID: P2-002
- Outcome: accepted after independent round-2 rereview
- Branch and commit: shared `main`; no commit created
- Files changed: owned deliverables listed above
- Benchmarks: not required; this contract/spike introduces no production performance threshold or platform-wide performance claim
- Deviations from card: none
- Normative questions discovered: none
- Known limitations within scope: spike types are deliberately not production APIs; platform evidence is the card-required local Windows safety/release gate
- Integration result: ADR AIBT-021 accepted, P2-002 marked Done, and direct dependents P2-006/P2-009 unlocked

Accepted coordinator-owned decisions row:

```markdown
| AIBT-021 | Use explicit native owners, immutable preflight capacity plans, staged atomic publication, and dependency-complete disposal defined by `Documentation~/decisions/ADR-P2-002-native-ownership-and-capacity.md`. | Accepted |
```

## Final self-check

1. Every card deliverable, review finding, and required local verification has candidate evidence.
2. No production storage, executor, global planning file, or Shared reducer semantic was introduced.
3. Claims are limited to the recorded Unity/Windows environment; independent round-2 acceptance is recorded without expanding them.
