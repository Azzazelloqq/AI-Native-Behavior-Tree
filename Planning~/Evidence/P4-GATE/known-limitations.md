# Known limitations after Phase 4

Prepared 2026-08-27 for the `P4-009` review.

## Carried forward from Phase 3, still true

- `Editor/Graph/`'s live window is not wired to anything Phase 3 built
  (`P3-003` through `P3-011` each host their own private view/window
  instance). Phase 4 did not touch `Editor/`; this is unchanged.
- No production Play-mode host exists to attach a debugger or trace view to a
  real running game. Unchanged.
- No production per-project leaf-behavior registration mechanism exists;
  every executable leaf is still a Phase 1 fixture or built-in composite/
  decorator. Unchanged, and now also the reason `P4-001`'s scenario catalog
  cannot exercise genuinely differentiated node-cost categories (see below).
- Large-graph editor performance is recorded (`P3-012`), not calibrated.
  Unchanged.
- Public API and persisted formats remain experimental below `1.0.0`.

## New in Phase 4, carried into Phase 5 and beyond

- **Only 6 of 14 catalog scenarios are measured end-to-end.** `P4-001`'s
  remaining 8 scenarios, and both `PipelinedJobs` and `Auto` as catalog
  entries, are documented placeholders in the result JSON. Extending the
  catalog is legitimate future work, not required for this gate.
- **`Auto` underperforms the best fixed policy in 23 of 24 measured cases**,
  by +188% to +1,774% ns/agent (`P4-006`). The root cause is traced (`Auto`'s
  decision tree unconditionally prefers `BatchedJobsSameFrame` for same-frame
  large workloads without accounting for `P4-002`'s own finding that fixed-
  batch-size `BatchedJobsSameFrame` does not amortize at these scales on this
  workstation) and is a specific, nameable defect in `P4-005`'s decision
  rule, fixable by deterministic recalibration -- legitimate follow-up work,
  not something this gate needed to fix, and not runtime adaptation (`P4-007`
  already closed that door).
- **One global work-estimation coefficient, not per-node-cost-category.**
  `P4-002`'s data supports only 6 scenario-level measurements, not a genuine
  per-node-type cost breakdown (blackboard-heavy, command-heavy, async-heavy
  leaves are not yet measured scenarios). If/when they are, recalibrating to
  per-category coefficients is future work `P4-004` explicitly anticipated
  but did not need to build.
- **Calibration is two devices, not a hardware-class generalization.**
  `NativeWorkEstimatorV1`'s coefficients are the pooled median of one Windows
  x64 workstation and one physical Android phone. Both the original 24-point
  Editor-based derivation and the superseding 42-point real-Player addendum
  are explicit that this is not generalized to other hardware
  (`Planning~/USER_ACTIONS.md` requires owner approval across hardware
  classes before any threshold or default is adopted).
- **`CalibrationTolerance` moved from 0.10 to 0.25** when the coefficient was
  recalibrated against real Player data (worst-case correlation error rose
  from 8.71% to 20.98%). A wider tolerance is an honest consequence of two
  architecturally different real devices being compared, not a sign the
  model correlates worse than before -- see `P4-004/README.md`'s addendum for
  the full breakdown by scenario and device.
- **A real defect was found and fixed outside the formal card workflow**:
  `NativeWorkEstimatorTests.cs`'s correlation test hardcoded literal Editor-
  measured values that were never updated when the coefficient was
  recalibrated in the same session, and would have failed by ~92% relative
  error the first time it actually ran. Fixed as part of `P4-004`'s addendum,
  not silently left for a future session to discover.
- **Web coverage is `Immediate`/`Budgeted` only**, a reduced parameter matrix
  (3 agent counts, fewer samples), and does not exercise Burst-compiled-to-
  WASM code (neither measured policy uses a `[BurstCompile]` job); several
  cases report `medianNsPerAgent: 0.000` due to browser timer-resolution
  limits (`P4-008`).
- **Android/Web coverage is one device/browser each.** Android is one
  physical Google Pixel 10 Pro (Android OS 17 / API-37); Web is whatever
  browser the Browser pane used. Neither generalizes to "Android" or "Web"
  broadly, and Safari/mobile Web remain entirely unmeasured (`OQ-004`, still
  open).
- **No regression threshold, scheduling default, or supported-hardware-class
  claim exists anywhere in the package.** This is Phase 4's own deliberate,
  disclosed scope boundary (item 6 of `P2-GATE/phase4-inputs.md`'s
  "required before any scheduling claim" list), not an oversight -- adopting
  one requires the owner's explicit approval per `USER_ACTIONS.md`, which has
  not been sought.

## Blocking nothing, recorded for completeness

- The remote `P0-005` Unity CI job remains queued, as it has since Phase 1;
  this was waived to start Phases 2, 3, and 4 and must not be reported as
  resolved.
