# Phase 4 claims inventory

Prepared 2026-08-27 for the `P4-009` review, against candidate commit
`9b9744443d9bbcaa3d4b3341343aeda818a26770`. Every supported claim below already
has committed evidence.

## Supported claims

- `Immediate`, `Budgeted`, and `BatchedJobsSameFrame` fixed policies have
  proven-flat, population-independent per-agent cost (`Immediate`/`Budgeted`)
  or a proven, traced, non-flat cost that grows with population at a fixed
  batch size (`BatchedJobsSameFrame`) -- measured, not assumed (`P4-002`).
- `PipelinedJobs` is semantically equivalent to the reference oracle: a
  golden-case equivalence matrix proves it, and genuine cross-stage latency is
  proven separately on real multi-round scenarios (`P4-003`).
- A single artificial cost spike in a synthetic input cannot move the
  smoothed work estimate by more than 12.5%, however extreme the spike (a
  100x synthetic spike proves the bound directly), and the calibrated
  coefficient correlates with real measured Player data within a documented,
  evidence-derived tolerance -- not an assumed threshold (`P4-004`, including
  its 2026-08-26 real-Player recalibration addendum).
- `NativeBatchSizeCalibrationV1`'s batch size clamps correctly at both the
  policy limit and the memory limit, with memory always winning conflicts, in
  constructed edge-case tests (`P4-004`).
- `Auto` deterministically selects among the four accepted policies from a
  genuine work estimate, a caller-supplied policy-capability set, and a full
  override surface, and explains every decision via fields with a real,
  verifiable data source -- not a faked field (`P4-005`).
- `Auto` was measured against the best same-frame-capable fixed policy across
  all 6 implemented `P4-001` scenarios at 4 agent-count points, and the
  result -- `Auto` underperforms in 23 of 24 cases -- is reported as measured,
  not tuned toward a better-looking outcome (`P4-006`).
- `OQ-006` (runtime autotuning vs. calibrated fixed heuristics) is resolved
  with real evidence: a working adaptive-tracker prototype was built and
  tested against a realistic single-observer feedback model, found to get
  permanently stuck on a cold-start mistake, and rejected on that basis, not
  by assumption (`P4-007`, `ADR-P4-007`, `AIBT-013`).
- All three mandatory pre-1.0 platforms (Windows x64, Android ARM64, single-
  thread Web) have real, non-Development, non-Editor Player benchmark
  evidence, including Android on a physical device confirmed genuine
  `arm64-v8a` via `adb` (`P4-008`).
- The release Player runs measurably faster than Editor batchmode on this
  workstation -- both overall (~13-14x, `P4-008`) and per atomic node-step
  specifically (~11x, `P4-004`'s addendum) -- and the work-estimation
  coefficient was recalibrated in direct response to that finding rather than
  left silently wrong.
- Windows x64 desktop and Android ARM64 mobile per-step real Player cost land
  within ~5% of each other despite architecturally different CPUs (61.82 vs.
  58.75 ns/step), evidence that a single pooled calibration constant
  generalizes reasonably across at least these two device classes.
- Unity `6000.5.8f1` compiles `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor`
  as a detached UPM installation (a fresh project referencing only the
  package and its declared dependencies) and passes 1060 EditMode tests with
  0 failed and 0 skipped.
- `P4-003`'s equivalence proof and `P4-005`'s determinism-on-rerun proof both
  re-run and pass individually within this gate's own detached-harness run,
  not merely cited from an earlier session.
- The public surface of `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor` at
  this commit is unchanged from `P3-GATE`: 382 types, 1994 members, byte-
  identical dump (`public-api.txt`/`.sha256`) -- Phase 4 added zero new public
  API surface.
- `AIBT.Runtime` and `AIBT.Authoring` reference neither `UnityEditor`, an MCP
  assembly, an LLM-provider assembly, nor `Unity.Entities`; `AIBT.Editor`
  depends on `Authoring`/`Runtime` only, never the reverse
  (`assembly-dependencies.json`).

## Claims intentionally not made

- Any performance default, regression threshold, or supported-hardware-class
  claim. Every P4 card's own "Forbidden changes" repeats
  `Planning~/USER_ACTIONS.md`'s requirement that thresholds and hardware-class
  approval come from the owner after the research exists, not from an
  implementation agent -- none has been sought or granted.
- That `Auto` is recommended, production-ready, or a sensible default policy
  choice. `P4-006`'s own measured result is the opposite in the large
  majority of cases; `Auto` exists as a measured, explainable, deterministic
  mechanism, not an endorsed one.
- That the recalibrated work-estimation coefficient (or its tolerance)
  generalizes beyond one Windows workstation and one physical Android phone.
  `P4-004`'s addendum explicitly scopes this as "one device per platform, not
  a generalization claim."
- That the full 14-scenario catalog, or `PipelinedJobs`/`Auto` specifically,
  are measured end-to-end. Only 6 of 14 scenarios have real end-to-end
  measurements (`P4-001`); the rest are documented placeholders, never
  silently substituted.
- Safari or mobile-Web platform support (`OQ-004`, still open and undischarged
  -- not claimed, not silently skipped).
- Multiple hardware classes per platform. Every platform in `P4-008` has
  exactly one measured device; "Windows x64 works" is not generalized to
  "all Windows x64 hardware," nor "Android ARM64" to all such devices.
- Anything about Phase 1/2/3's own runtime, editor, or platform claims beyond
  what `P2-GATE`/`P3-GATE` already recorded -- this gate does not re-litigate
  either accepted gate.
- Stable public API compatibility beyond the recorded experimental `0.1.0`
  baseline.
