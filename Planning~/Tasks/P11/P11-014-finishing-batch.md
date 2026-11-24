# P11-014 — Finishing batch: review follow-ups of P11-006 to P11-013

Status: `in_progress` — committed on `feature/aibt-consumer-workflow` (local commits only); waits for the
orchestrator's acceptance run.

## Objective

Close what the production-readiness review (`Planning~/Evidence/P11-REVIEW/README.md`) and the
independent review of P11-012 and P11-013 left open, as the orchestrator's brief of 2026-11-24 lists it:

- **A. Fix:** the observer rule in MCP `validate`/`compile`, the bake and the built-in-only warning
  (items 1-3); `create_tree` defaults to format 2 (W10, item 4); a failed import reports its own errors
  first (W3, item 5); `ProductionTreeHost.IsExecuting` public (W6, item 6); a true message and a written
  rule for the value-session limit (item 7); two wrong comments (item 8); aliased-key tests that tell a
  lost write from an applied one (item 9).
- **B. Confirm with one test each, then act:** U3, U4, U6, U9, U10.
- **C. Document:** N1, N5, N8 and a Known limitations page.
- **D. Record:** a table of every second-tier finding with its class and reason.
- **E. From the independent review of P11-012 and P11-013:** a finished host failed by the scheduler under
  forced `PipelinedJobs` (item 10), missing and vacuous tests (item 11), the combine function's description
  (item 12), ADR-P11-013 and guide text (item 13), the inspector hint cache (item 14), the unaligned Agent
  read record and the `default` seed literal (item 15).

## Depends on

- `P11-013` (and through it P11-007 to P11-012).

## Scope

Runtime, Editor, MCP and test files named per item in the evidence; `Documentation~/` (guides, the new
`known-limitations.md`, generated references through their generators), `Samples~/NativeGuard/README.md`,
`README.md`, `CHANGELOG.md`, `Planning~/`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  `package.json`, pins and existing fixtures (including the P11-009 fixture nodes), normative
  specifications other than those the brief names, existing public signatures (only additions).

## Acceptance criteria

- Behaviour changes test first (failing before), one commit per item or per small group of one kind.
- Every confirmation has one test and its outcome with numbers in the evidence.
- Only the touched tests are run (not the ten assemblies); the Console free of new errors; the submodule
  checkout clean; `Tools~/Verification/Verify-Static.ps1` passes with 193 work items.
- `Planning~/Evidence/P11-014/README.md` with raw files in `C:\UnityProjects\_aibt_acceptance\P11-014\`.

## Evidence

`Planning~/Evidence/P11-014/README.md`.
