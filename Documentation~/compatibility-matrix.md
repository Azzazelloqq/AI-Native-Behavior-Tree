# Supported-platform matrix and regression thresholds

> **Status: ACCEPTED** — approved by the owner as-is on 2026-09-02 (recorded in
> `Planning~/Evidence/P7-002/README.md`), satisfying `Planning~/USER_ACTIONS.md`'s "Approve the
> exact supported browser/version policy" and "Approve performance hardware classes and acceptable
> regression thresholds" bullets. This page is now the reference every later platform claim
> (README, samples, release notes) should point at rather than restating numbers inline.

## Platform matrix

| Platform | Functional conformance | Real-hardware performance numbers | Status |
|---|---|---|---|
| Windows x64 | Measured — `Planning~/Evidence/P2-WINDOWS/README.md` (7 behavior-matrix cases, 0 Burst fallback, IL2CPP) | Measured — `Benchmarks~/Phase4/Platform/Windows/Results/windows-player-scheduling-20260821.json` (real `StandaloneWindows64` Player); calibration in `Planning~/Evidence/P4-004/README.md`'s 2026-08-26 addendum (61.82 ns/step, 24 samples) | **Supported** |
| Android ARM64 | Measured — `Planning~/Evidence/P2-ANDROID/README.md` (build/AOT conformance only, no device) | Measured — `Benchmarks~/Phase4/Platform/Android/Results/android-player-scheduling-20260821.json`, real physical **Google Pixel 10 Pro**; calibration 58.75 ns/step, 18 samples (`P4-004` addendum) | **Supported** |
| Web — Chrome (desktop) | Measured — `Planning~/Evidence/P2-WEB/README.md`, `Planning~/Evidence/P0-003/README.md` (Chrome 151.0.7922.13x) | Partially measured — `Benchmarks~/Phase4/Platform/Web/Results/web-player-scheduling-20260821.json`, Immediate/Budgeted only (`BatchedJobsSameFrame`/`PipelinedJobs` excluded from Web's own policy scope); many samples read `0.000` ns/agent due to browser `Stopwatch` coarseness | **Supported, latency-bound policies only** |
| Web — Firefox (desktop) | Measured — same two evidence files (Firefox 153.0.4) | Not separately Player-benchmarked (P4-008's Web leg did not distinguish browser) | **Supported (functional), performance number is Chrome-only** |
| Web — Safari (desktop) | **Unmeasured** | **Unmeasured** | **Explicitly unsupported** — `USER_ACTIONS.md`: "Provide access to macOS/Safari hardware or CI before Safari can become a verified Web target." No macOS host has ever been available to this project. |
| Web — mobile browsers (any) | **Unmeasured** | **Unmeasured** | **Explicitly unsupported** — disclosed in both `P2-WEB` and `P0-003`'s own evidence, not silently omitted. |
| Console (any) | **Unmeasured** | **Unmeasured** | **Explicitly unsupported** — `USER_ACTIONS.md`: "Provide console platform access before any console support claim." |

## Player size versus tree count — measured Windows scope

P7-026 measured two Windows x64 IL2CPP release Players on Unity 6000.5.8f1 with the same node
catalog: 1 versus 100 authored JSON trees. Raw shipped files grew from **87,958,367 to 87,994,395
bytes** (+36,028, about 0.041%). All code binaries and IL2CPP metadata were byte-identical; growth
was confined to serialized Resources data and its index. Both Players loaded and compiled every
expected tree and matched input hashes and catalog fingerprint.

This supports absence of per-tree code growth in this fixed-catalog comparison, not constant
total Player size. The probe ships Authoring plus JSON; runtime-only precompiled packaging,
catalog-size scaling, archive/download size and Android/Web size are unmeasured. Reproduction,
inventories and exact attribution: [P7-026 evidence](../Planning~/Evidence/P7-026/README.md).

## Regression-threshold proposal

Real spread from the Windows Player results (`windows-player-scheduling-20260821.json`,
`scheduling-baseline-empty-job` scenario, 16 agents — the smallest tested population, so the
*most* forgiving case for a batching policy):

| Policy | Median (ns/agent) | Max (ns/agent) | Max vs. median |
|---|---:|---:|---:|
| Immediate | 218.75 | 237.5 | +8.5% |
| Budgeted | 231.25 | 256.25 | +10.8% |
| BatchedJobsSameFrame | 4631.25 | 19162.5 | **+313.7%** |

This is not scale-dependent noise — it reproduces `P4-002`'s own Editor-side finding
(`Planning~/Evidence/P4-002/README.md`: batch/chunk overhead does not amortize with population) at
the Player level, on the smallest population tested. It is also consistent with `P4-006`'s own
finding that `Auto` underperforms a correctly-chosen fixed policy in 23 of 24 measured cases
(`Planning~/Evidence/P4-006/README.md`), gaps of +188% to +1,774% — batching-family policies are
simply high-variance by construction in this engine's current design, not a defect to threshold
away.

**Proposed structure** (per-policy, not one uniform number — a single threshold would be either
meaningless for `Immediate`/`Budgeted` or a permanent false-positive generator for the batched
policies):

- **Immediate / Budgeted**: a **20% median-over-median** regression threshold — comfortably above
  the ~9–11% single-run max spread actually observed, leaving margin against noise while still
  catching a real regression.
- **BatchedJobsSameFrame / PipelinedJobs**: a single-run percentage threshold is not meaningful
  given the observed 300%+ intra-run spread at the smallest tested population. Proposed instead:
  compare the **median of several runs** (e.g. 5+) against a similarly multi-run baseline, never a
  single sample either side. Left as an explicit open question for the owner rather than inventing
  a number that would be false-positive-prone by construction.
- **Android**: the existing raw JSON (`android-player-scheduling-20260821.json`) records median
  only, no min/max spread field — there is no data to derive a threshold from yet. Disclosed as a
  real measurement gap, not assumed equal to Windows'.

## Acceptance record

Approved by the owner as-is on 2026-09-02, per `P7-002`'s own acceptance criteria requiring the
actual decision to be recorded, not assumed. See `Planning~/Evidence/P7-002/README.md` for the
verbatim record. The open question on batched-policy regression comparison (multi-run median, no
concrete run count yet fixed) remains open — accepting the matrix as-is did not resolve it; a
future card that actually wires regression detection into CI should pick a concrete run count then.
