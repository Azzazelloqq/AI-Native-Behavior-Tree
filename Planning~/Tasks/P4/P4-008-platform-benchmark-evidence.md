# P4-008 — Platform benchmark evidence

Status: `Draft`

## Objective

Run `P4-001`'s full scenario/parameter matrix against the finalized scheduler (fixed policies, `PipelinedJobs`, `Auto`, and whatever `P4-007` resolved for autotuning) on the mandatory pre-1.0 platform targets, per `Documentation~/benchmarks.md`'s "Platform process" and mirroring Phase 2's own platform-evidence shape.

## Depends on

- `P4-005`.
- `P4-007`.

## Required reading

- `Documentation~/benchmarks.md` ("Platform process").
- `Planning~/Evidence/P2-WINDOWS/`, `P2-ANDROID/`, `P2-WEB/` (the shape this card mirrors).
- `Planning~/USER_ACTIONS.md` ("Required before public 1.0 claims" — hardware-class and threshold approval gating).

## Allowed changes

- `Benchmarks~/Phase4/Platform/` (new, mirrors `Benchmarks~/Phase2/`'s per-platform structure).
- `Planning~/Evidence/P4-008/`.

## Forbidden changes

- Any regression threshold or performance default. `Planning~/USER_ACTIONS.md` requires owner approval of hardware classes and thresholds *after* this research exists, not as a byproduct of running it.
- Any claim beyond the specific hardware actually measured (mirrors `benchmarks.md`'s own "Desktop development results cannot be generalized to mobile, console, or WebGL" rule).

## Deliverables

- Windows x64 Player benchmark results across the full scenario matrix.
- Android ARM64 benchmark results (device class per `USER_ACTIONS.md`'s "Identify at least one Android ARM64 device class for benchmark evidence").
- Single-thread Unity Web benchmark results (desktop supported browsers), covering unmanaged immediate execution, deterministic step budgeting, Burst WASM feasibility, memory pressure, build size, and browser throttling, per `benchmarks.md`.

## Acceptance criteria

- Every platform's results record Unity/package/OS/CPU architecture, logical and worker counts, build configuration, and scenario revision.
- Results from one browser/device are never presented as establishing support for another.
- No regression threshold or "supported" performance claim is introduced by this card; it produces evidence for the owner-approval step `USER_ACTIONS.md` requires, not the approval itself.

## Required verification

```text
Verify-Static.ps1
full P4-001 harness run on Windows x64, Android ARM64, and single-thread Unity Web
```

## Handoff notes

- Mandatory pre-1.0 targets only (Windows x64, Android ARM64, single-thread Unity Web), per `OPEN_QUESTIONS.md`'s closed items. Safari/mobile Web and console targets remain out of scope pending `OQ-004` and `USER_ACTIONS.md`'s console-access item.
- `P4-009` (the Phase 4 gate) is the point where the owner is asked to approve hardware classes and thresholds from this evidence, per `USER_ACTIONS.md` -- this card does not request that approval itself.
