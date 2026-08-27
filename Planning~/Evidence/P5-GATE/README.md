# Phase 5 integration gate status

Checkpoint: 2026-08-27, Unity `6000.5.8f1`. Gate executed and accepted
2026-08-27 against commit `42a32eab7953944823401eccb40b8b60a5c94bfd`.

## Verdict: Accepted

Every step in `gate-runbook.md` ran against a clean detached snapshot and
passed. Full machine-readable results are in `verification-results.json`.
Summary:

- `P5-001` through `P5-009` complete: `OQ-007` resolved with real-code
  evidence and a live-verified spike (`P5-001`), an inspectable node-identity/
  layout model and compatibility classifier (`P5-002`/`P5-003`), safe full
  restart for the reference-executor backend (`P5-004`), localized subtree
  restart and idle-instance compatible migration built as one mechanism per
  `ADR-P5-001`'s own correction (`P5-005`/`P5-006`), a scheduler-estimator
  reset decision made and tested with the remaining native-backend-scoped
  criteria disclosed as unmet (`P5-007`), an explicit, explained, live-verified
  Editor hot-reload workflow (`P5-008`), and real Editor-plus-Player benchmark
  evidence showing migration measurably ~1.9-2x cheaper than full restart
  (`P5-009`);
- Phase 4 gate's constraints Phase 5 must not violate are all confirmed still
  true (`contract-checklist.md`);
- clean detached-UPM-harness compile: a fresh project containing nothing but
  `com.azzazello.aibt` (as a local `file:` package) and its declared
  dependencies, exit code 0;
- full detached-package EditMode: **1089/1089**, 0 failed, 0 skipped (XML
  SHA-256 `537c92ec7c5408c917add8d375447f0144eca4adea3b552be4384c2c1a8b1507`);
  the pre-existing host-project-only failures seen in every prior P3/P4
  evidence file did not reproduce here, confirming host-project noise, not
  AIBT defects -- the same pattern `P3-013`/`P4-009` found;
- every Phase 5 test fixture (`P5-002` through `P5-006`, `P5-008`) re-run and
  passed individually against this committed snapshot, not merely cited from
  an earlier session -- see `gate-runbook.md` step 5;
- `OQ-007` confirmed `Resolved` in `Planning~/OPEN_QUESTIONS.md`, linked to
  `ADR-P5-001` (`AIBT-023`, `Accepted 2026-08-27`) via `Documentation~/decisions.md`;
- public API surface recorded for all three production assemblies -- **391
  types (+9), 2024 members (+30) versus `P4-GATE`'s 382/1994** -- confirmed
  by `diff` to be **purely additive**, no removed or changed existing member:
  `AIBT.Authoring.HotReloadPreviewDriver`, `AIBT.Authoring.HotReloadPreviewOutcome`,
  `AIBT.Editor.HotReload.HotReloadWorkflowWindow`, `AIBT.HotReloadClassificationResult`,
  `AIBT.HotReloadCompatibilityClassifier`, `AIBT.HotReloadNodeIdentitySignature`,
  `AIBT.HotReloadNodeVerdict`, `AIBT.HotReloadNodeVerdictCategory`, and
  `AIBT.HotReloadProgramIdentityMap`, plus their members -- unlike Phase 4,
  which added zero, Phase 5 legitimately needed a public, inspectable
  classification/identity model and cross-assembly-boundary facades;
- assembly dependency audit: `AIBT.Runtime`/`AIBT.Authoring` reference
  neither `UnityEditor`, MCP, an LLM provider, nor `Unity.Entities`;
  `AIBT.Editor` depends on `Authoring`/`Runtime` only, never the reverse;
- `git status`/`git rev-parse HEAD` clean at every checkpoint (HEAD unchanged
  at the candidate commit throughout);
- `README.md` and `CHANGELOG.md` were found stale (still describing Phases
  1-4 as complete and omitting Phase 5 entirely) and updated to reflect
  actual completion, without introducing any new default, threshold, or
  supported-reload-scale claim -- verified against `claims-inventory.md`
  afterward.

Two real, disclosed scope reductions were made during this Phase 5 cycle,
both by explicit owner decision, neither hidden by this gate: (1) hot reload
is scoped to the reference-executor backend only -- native-backend hot reload
does not exist, leaving `P5-007`'s golden-equivalence/batch-isolation/`Auto`-
determinism criteria genuinely unmet for a hot-reloaded instance; (2)
compatible/subtree migration only runs when the old instance is idle, falling
back to full restart for a genuinely active one, because `ReferenceFrame`'s
read-only `NodeIndex` and extensive per-decorator mutable fields made full
mid-flight active-frame-stack migration substantially larger than
`ADR-P5-001` originally anticipated. Both are recorded in
`known-limitations.md` and in the affected cards' own evidence, not smoothed
over by this gate.

**Phase 5 is complete**, with the native-backend hot-reload and
active-mid-flight-migration gaps above explicitly disclosed as scoped-out
rather than silently missing.

## Gate package

| Document | Purpose |
| --- | --- |
| `contract-checklist.md` | Each Phase 5 card's own contract, and Phase 4's constraints Phase 5 must not violate, mapped to its evidence |
| `claims-inventory.md` | Exactly what Phase 5 claims and what it deliberately does not |
| `known-limitations.md` | Scope limits carried into Phase 6 and beyond |
| `gate-runbook.md` | The verification commands actually executed, and their actual results |
| `assembly-dependencies.json` | Per-asmdef reference audit against the forbidden-dependency list |
| `public-api.txt` / `.sha256` | Reflected public surface of `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor` at the accepted commit (additive-only versus `P4-GATE`) |
| `verification-results.json` | Machine-readable result of every gate-runbook step |
| `phase6-inputs.md` | What Phase 6 additionally inherits from Phase 5, on top of `P3-GATE`/`P4-GATE`'s own handoffs |
