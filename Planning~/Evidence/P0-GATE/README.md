# Phase 0 integration gate evidence

Reviewed on 2026-08-14 against candidate `5c8d7f4a79fbec5cb5beb8090904b826d3b61365`. Result: **blocked on one external CI dependency**.

## Predecessor matrix

| Work item | Index state | Evidence review | Gate result |
| --- | --- | --- | --- |
| P0-001 | Done | Exact Unity `6000.5.8f1` isolated package compile evidence exists. Parent-project UniTask issue remains explicitly unrelated. | Accepted for this gate. |
| P0-002 | Done | Static, compile, focused/full EditMode, and controlled failure evidence exists. | Accepted for this gate. |
| P0-003 | Done | Non-development IL2CPP WebGL build, Chrome/Firefox golden behavior, full immediate/budgeted semantic equivalence, and an accepted single-thread backend ADR exist. | Accepted. |
| P0-004 | Done | The final P1-018 snapshot builds as an unsigned Android IL2CPP ARM64-only APK with Burst enabled. APK inspection proves both IL2CPP and Burst ARM64 libraries; device execution remains explicitly unverified. | Accepted for this gate. |
| P0-005 | Review | Remote static job passes. Clean detached clone passes Unity compile and 580/580 EditMode locally. The remote Unity job remains queued without a matching self-hosted runner. | Blocking. |

## Repository and user-action state

- OQ-002 is resolved by GitHub Actions plus a pre-activated self-hosted Unity runner. No workflow license secret is defined.
- OQ-003 is resolved by the accepted `SingleThreadImmediate` / `SingleThreadBudgeted` Web ADR.
- Android device execution and Safari remain unverified and are not claimed.
- A detached clean clone of candidate `5c8d7f4` passed static/schema verification, Unity package compile, and 580/580 EditMode tests with zero skips.
- P1-018 has an accepted implementation review and a final isolated EditMode result of 580/580, so it no longer blocks starting P0-003.

## Dependency frontier

The machine-readable graph requires:

1. connect the documented pre-activated self-hosted Unity runner;
2. obtain a passing P0-005 GitHub workflow run;
3. record the remote run and move P0-005, P0-006, and P1-019 to `Done` without changing semantics.

P1-019 remains downstream of both P1-018 and this gate. No Phase 0 completion claim is valid before the blockers above close.
