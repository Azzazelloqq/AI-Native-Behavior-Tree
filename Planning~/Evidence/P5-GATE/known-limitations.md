# Known limitations after Phase 5

Prepared 2026-08-27 for the `P5-010` review.

## Carried forward from earlier phases, still true

- `Editor/Graph/`'s live window is not wired to anything Phase 3, 4, or 5
  built; every preview/debugger/trace/hot-reload surface hosts its own
  private view/window instance. Unchanged.
- No production Play-mode host exists to attach a debugger, trace view, or
  hot-reload trigger to a real running game. Unchanged.
- No production per-project leaf-behavior registration mechanism exists;
  every executable leaf used across every phase's evidence is still a Phase 1
  fixture or built-in composite/decorator. Unchanged.
- Only 6 of 14 `P4-001` catalog scenarios are measured end-to-end; `Auto`
  underperforms the best fixed policy in most measured cases (`P4-006`,
  `P4-007`, disclosed and not this phase's concern to fix). Unchanged.
- Calibration remains two devices (one Windows workstation, one Android
  phone), not a hardware-class generalization. Unchanged.
- Public API and persisted formats remain experimental below `1.0.0`.

## New in Phase 5, carried into Phase 6 and beyond

- **Native-backend hot reload does not exist.** Every Phase 5 mechanism
  (`P5-004` full restart, `P5-005`/`P5-006` migration) is built for the
  reference-executor backend only. `NativeInstanceArenaOwner.TryDispose`/
  `NativeProgramImageOwnerV1.TryCreate` exist and are proven from Phase 2, but
  wiring a hot-reload-specific wrapper needs the capacity-plan/lease preflight
  machinery `P2-002`/`AIBT-021` describe, which was not built here. This was
  an explicit, escalated owner decision (`P5-007`'s gap), not an oversight --
  disclosed rather than approximated against the wrong backend.
- **Migration only runs when the old instance is idle.** `ReferenceFrame`'s
  read-only `NodeIndex` and the extensive per-decorator/parallel/repeater/
  cooldown mutable fields on the live call-stack made full active-frame-stack
  migration substantially larger than `ADR-P5-001` originally anticipated.
  The owner decided to scope migration to idle-instance state only
  (memory/generation/cooldown-flags/blackboard), falling back to full restart
  whenever the old instance is genuinely mid-execution. Full mid-flight
  active-frame migration is real, disclosed follow-up work, not silently
  dropped -- see `ADR-P5-001`'s implementation addendum.
- **`P5-007`'s scheduler-interaction acceptance criteria are only partially
  met.** The estimator reset-vs-carry-over decision is made, tested, and
  correct (reset, never carried over). Its remaining criteria -- a
  golden-equivalence re-run, batch isolation, and `Auto` determinism, all
  for a *hot-reloaded* instance -- describe the native backend specifically,
  which has no reload mechanism to test (see above). Disclosed as a real,
  load-bearing gap in `Evidence/P5-007/README.md`, not faked against the
  reference-executor backend instead.
- **Debug-instrumentation overhead (trace capture during a reload) is not
  measured.** `HotReloadPreviewDriver` -- the only entry point Phase 5's own
  benchmark card was allowed to drive -- hardcodes `traceSink: null` in every
  reload call, with no parameter to inject a real `IReferenceTraceSink`.
  Measuring this needs either an internals-visible test assembly or a public
  API change to `HotReloadPreviewDriver`, both outside `P5-009`'s own
  allowed/forbidden-changes fence. Real future work, not approximated here.
- **Reload cost does not amortize across a population of live instances.**
  `P5-009`'s supplementary measurement found reloading 1, 10, or 50
  independently created instances against the same new document costs the
  same per-instance amount regardless of population size -- there is no
  batched-reload API that compiles/classifies a change once and applies it to
  many live instances sharing one behavior tree. A real, disclosed
  architecture characteristic and a legitimate future optimization
  opportunity, not a defect this phase needed to fix.
- **The Editor hot-reload workflow (`P5-008`) is its own private window**,
  inheriting the same "not wired into `Editor/Graph/`'s live window"
  limitation every Phase 3 editor card carried; `P5-008` did not change that
  boundary, per its own forbidden-changes clause.
- **Phase 5 legitimately added new public API surface** (`HotReloadPreviewDriver`,
  `HotReloadPreviewOutcome`, `HotReloadWorkflowWindow`,
  `HotReloadCompatibilityClassifier`, `HotReloadClassificationResult`,
  `HotReloadNodeVerdict`/`HotReloadNodeVerdictCategory`,
  `HotReloadProgramIdentityMap`, `HotReloadNodeIdentitySignature`) -- unlike
  Phase 4, which added zero. This is expected: Phase 5's own cards required
  an inspectable classification/identity model and cross-assembly-boundary
  facades. See `README.md`'s Verdict section for the full diff.
- **No regression threshold, "acceptable reload cost," or supported-reload-
  scale claim exists anywhere in the package.** Every P5 card's own
  "Forbidden changes" restates `Planning~/USER_ACTIONS.md`'s requirement that
  such a claim needs the owner's explicit approval, which has not been
  sought.

## Blocking nothing, recorded for completeness

- The remote `P0-005` Unity CI job remains queued, as it has since Phase 1;
  this was waived to start Phases 2 through 5 and must not be reported as
  resolved.
