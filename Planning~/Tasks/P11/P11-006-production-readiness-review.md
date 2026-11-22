# P11-006 — Production-readiness review of P11-001 … P11-005

Status: `Review` — the report is written. The orchestrator checks it and brings the verdict to the owner.

## Objective

The owner asked for an independent review of everything done in P11-001 … P11-005
(`git diff 2e6b9c7..ad1e581`), in his words: check that performance and code are done right, that
nothing is missing, and that it can be taken to production. The review gives a verdict it can defend
and evidence for every finding.

This is a review card. It records findings and fixes nothing. Product code, tests, documentation and
specifications are unchanged.

## Depends on

- `P11-001`, `P11-002`, `P11-003`, `P11-004`, `P11-005`.

## Scope

1. **Code correctness:** all production, sample, schema and package files of the diff.
2. **Performance:** runtime per tick, authoring, and Editor. Measured in the Editor only where reading
   left a doubt.
3. **Completeness:** each ADR and task card against the code and tests; every "not done" list; test
   quality; the user documentation.
4. **Known risks:** the seven known risks, each with its facts, cost, likelihood and recommendation.
5. **Failing tests:** the "known" failing test `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`
   and the two `GeneratedArtifactContractTests` failures.

## Rules followed

- Read-only for the product.
- No checkout of another commit.
- No Player build.
- No process killed.
- Probes ran through `unity command eval_file` against the Modules Editor (port 7810), so neither
  checkout received a probe file. Probe sources and raw outputs are in
  `C:\UnityProjects\_aibt_acceptance\P11-review\probes\`.

## Outcome

`Planning~/Evidence/P11-REVIEW/README.md`.

**Verdict:** `ready with conditions`. There is no blocker.

| Severity | Findings |
|---|---|
| Fix before release | 14 |
| Can wait | 15 |
| Note | 8 |
| Unconfirmed | 10 |

**The three most important findings:**
- **F1:** one key bound to two handles of one native node compiles, then the host faults on every
  update (run).
- **F2:** a format-3 tree without references, for example after Unpack, cannot be compiled natively
  (run).
- **F3–F5:** Editor paths scan the whole project tree on disk per frame, per tree or per reload
  (measured at about 85 ms per scan).

**The three "known" failing tests:** all three are stale tests; the product is correct.

## Acceptance criteria

- The report exists with:
  - the verdict;
  - findings sorted by severity, each with `file:line`, a scenario, how it was verified and a fix;
  - one section per review question;
  - the performance section;
  - the prioritised to-do list;
  - the "not reviewed" list.
- `Tools~/Verification/Verify-Static.ps1` passes with 186 work items.
- Both checkouts are left clean, except for the commit of this card, the work item, the
  `MASTER_PLAN.md` sentence and the report.
