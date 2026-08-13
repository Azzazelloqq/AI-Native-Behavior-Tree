# Phase 0 integration gate evidence

Reviewed on 2026-08-14. Result: **blocked**.

## Predecessor matrix

| Work item | Index state | Evidence review | Gate result |
| --- | --- | --- | --- |
| P0-001 | Done | Exact Unity `6000.5.8f1` isolated package compile evidence exists. Parent-project UniTask issue remains explicitly unrelated. | Accepted for this gate. |
| P0-002 | Done | Static, compile, focused/full EditMode, and controlled failure evidence exists. | Accepted for this gate. |
| P0-003 | Index says Done; card remains Draft | No Web Player/browser measurements or accepted backend ADR exist. P1-018 is now accepted, so its semantic dependency is ready. | Blocking: evidence and ADR absent. |
| P0-004 | Done | The final P1-018 snapshot builds as an unsigned Android IL2CPP ARM64-only APK with Burst enabled. APK inspection proves both IL2CPP and Burst ARM64 libraries; device execution remains explicitly unverified. | Accepted for this gate. |
| P0-005 | Review | Workflow and local checks exist, but no successful GitHub workflow run exists yet. | Blocking. |

## Repository and user-action state

- OQ-002 is resolved by GitHub Actions plus a pre-activated self-hosted Unity runner. No workflow license secret is defined.
- OQ-003 remains evidence-driven and unresolved until P0-003 produces a Web backend ADR.
- Android device execution and Safari remain unverified and are not claimed.
- The shared worktree contains active Phase 1 changes, so the required clean-checkout gate run has not been performed.
- P1-018 has an accepted implementation review and a final isolated EditMode result of 580/580, so it no longer blocks starting P0-003.

## Dependency frontier

The machine-readable graph requires:

1. run and accept P0-003 Web evidence and backend ADR;
2. obtain a passing P0-005 GitHub workflow run;
3. rerun P0-006 from a clean checkout with initialized submodules.

P1-019 remains downstream of both P1-018 and this gate. No Phase 0 completion claim is valid before the blockers above close.
