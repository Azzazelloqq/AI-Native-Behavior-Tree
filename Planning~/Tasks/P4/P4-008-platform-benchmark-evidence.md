# P4-008 — Platform benchmark evidence

Status: `Done`

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

## Outcome

Built and ran a real, non-development, IL2CPP, Burst-enabled Windows x64 Standalone Player
containing `P4-001`'s exact scenario/policy sweep. **Major finding**: the release Player runs the
same scenarios ~13-14x faster than Editor batchmode, consistently across every scenario and agent
count — meaning every Editor-measured number in `P4-001`/`P4-002`/`P4-005`/`P4-006`/`P4-007`'s
evidence understates real release performance by roughly an order of magnitude on this
workstation. `BatchedJobsSameFrame`'s fixed-batch-size overhead (traced by `P4-002`/`P4-006`)
reproduces in the Player too, confirming it is a genuine scheduling-code property, not an Editor
artifact.

After that first pass, the user asked whether Android/Web were actually feasible rather than
assumed unavailable — checking properly found both Unity's `AndroidPlayer`/`WebGLSupport` modules
installed and a Browser pane able to genuinely run a WebGL build. **Web was measured**: a real,
single-thread WebGL Player (`Immediate`/`Budgeted` only, per this backend's own accepted policy
scope) ran in an actual browser, after fixing a real `Content-Encoding: gzip` hosting mismatch
(`PlayerSettings.WebGL.decompressionFallback`) and disclosing a genuine browser-timer-resolution
limitation (several cases below measurable resolution, reported honestly, not as zero cost).

**Android ARM64 was measured too**, on the user's own physical Google Pixel 10 Pro: only an
x86_64 system image/AVD was available locally, which does not satisfy `USER_ACTIONS.md`'s
ARM64-device-class requirement, so the user connected their own phone over `adb` (confirmed
genuine `arm64-v8a`) instead. A real, non-development, IL2CPP, Burst-enabled Android Player ran
all three fixed policies there. Two notable findings: this Windows workstation is only
~1.1x-1.3x faster than the phone for `Immediate` (much closer than the Editor-vs-Player gap
suggested), and `BatchedJobsSameFrame`'s fixed-batch-size overhead reproduces at roughly the same
~18x-23x magnitude on ARM64 mobile silicon as it does on Windows (~21x-29x) — confirming
`P4-002`/`P4-006`'s traced mechanism is a property of the scheduling code's interaction with
Unity's Job system, not tied to one CPU architecture or OS. The test app was uninstalled from the
user's phone immediately after capturing results.

All three mandatory pre-1.0 targets are now measured. No threshold or default is introduced by
any of this — see `Planning~/Evidence/P4-008/`, `Benchmarks~/Phase4/Platform/Windows/README.md`,
`Benchmarks~/Phase4/Platform/Web/README.md`, and `Benchmarks~/Phase4/Platform/Android/README.md`.
