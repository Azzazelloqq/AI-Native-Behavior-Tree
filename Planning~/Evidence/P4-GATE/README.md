# Phase 4 integration gate status

Checkpoint: 2026-08-27, Unity `6000.5.8f1`. Gate executed and accepted
2026-08-27 against commit `9b9744443d9bbcaa3d4b3341343aeda818a26770`.

## Verdict: Accepted

Every step in `gate-runbook.md` ran against a clean detached snapshot and
passed. Full machine-readable results are in `verification-results.json`.
Summary:

- `P4-001` through `P4-008` complete, including the scenario/harness catalog
  (`P4-001`), fixed-policy cost curves showing why batching calibration is
  necessary (`P4-002`), a proven-equivalent `PipelinedJobs` executor
  (`P4-003`), a calibrated work-estimation/batching model recalibrated
  against real Player data mid-Phase (`P4-004`), an explainable deterministic
  `Auto` heuristic (`P4-005`), an honest `Auto`-vs-fixed comparison showing
  `Auto` underperforming in most measured cases (`P4-006`), `OQ-006`'s
  resolution rejecting runtime autotuning on tested evidence (`P4-007`), and
  real non-Editor Player benchmark evidence on all three mandatory pre-1.0
  platforms (`P4-008`);
- the Phase 2 gate's six "required before any scheduling claim" items are
  closed or correctly left open per their own stated precondition
  (`contract-checklist.md`);
- clean detached-UPM-harness compile: a fresh project containing nothing but
  `com.azzazello.aibt` (as a local `file:` package) and its declared
  dependencies, exit code 0;
- full detached-package EditMode: **1060/1060**, 0 failed, 0 skipped (XML
  SHA-256 `3a4e7e6c58c34b24665c07b5a6379d57feaf906864345bc5626866d6dfb416e5`);
  the 3 failures repeatedly seen inside the host `Modules` project did not
  reproduce here, confirming they were host-project noise, not AIBT defects
  (the same pattern `P3-013` found for its own 3 host failures);
- `P4-003`'s `PipelinedJobs` equivalence proof and `P4-005`'s determinism-on-
  rerun proof both re-run and passed individually against this committed
  snapshot, not merely cited from an earlier session;
- `OQ-006` confirmed `Resolved: rejected` in `Planning~/OPEN_QUESTIONS.md`,
  linked to `ADR-P4-007` (`Accepted 2026-08-21`) via `Documentation~/decisions.md`;
- public API surface recorded for all three production assemblies -- **382
  types, 1994 members, byte-identical to `P3-GATE`'s own dump**: Phase 4
  added zero new public API surface, consistent with its work being entirely
  internal scheduling/native-execution machinery;
- assembly dependency audit: `AIBT.Runtime`/`AIBT.Authoring` reference
  neither `UnityEditor`, MCP, an LLM provider, nor `Unity.Entities`;
  `AIBT.Editor` depends on `Authoring`/`Runtime` only, never the reverse;
- `git status`/`git rev-parse HEAD` clean at every checkpoint (HEAD unchanged
  at the candidate commit throughout);
- `README.md` and `CHANGELOG.md` were found stale (still describing the
  `P2-025` gate as in-progress, omitting Phase 3 and Phase 4 entirely) and
  updated to reflect actual completion, without introducing any new default,
  threshold, or supported-hardware-class claim -- verified against
  `claims-inventory.md` afterward.

One real defect was found and fixed during this Phase 4 cycle, outside the
formal gate itself: `P4-004`'s work-estimation coefficient was originally
calibrated from Editor batchmode data and, once `P4-008`'s platform work
found the release Player runs ~11-14x faster, was recalibrated against real
Windows/Android Player data -- which in turn revealed a stale, hardcoded test
fixture that would have failed by ~92% relative error the first time it
actually ran. Both are documented in `Planning~/Evidence/P4-004/README.md`'s
2026-08-26 addendum and confirmed fixed by this gate's own full-suite re-run.

**Phase 4 is complete.**

## Gate package

| Document | Purpose |
| --- | --- |
| `contract-checklist.md` | Each Phase 4 contract, and Phase 2's six handoff items, mapped to its evidence |
| `claims-inventory.md` | Exactly what Phase 4 claims and what it deliberately does not |
| `known-limitations.md` | Scope limits carried into Phase 5 and beyond |
| `gate-runbook.md` | The verification commands actually executed, and their actual results |
| `assembly-dependencies.json` | Per-asmdef reference audit against the forbidden-dependency list |
| `public-api.txt` / `.sha256` | Reflected public surface of `AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor` at the accepted commit (unchanged from `P3-GATE`) |
| `verification-results.json` | Machine-readable result of every gate-runbook step |
| `phase5-inputs.md` | What Phase 5 additionally inherits from Phase 4, on top of `P3-GATE/phase5-inputs.md` |
