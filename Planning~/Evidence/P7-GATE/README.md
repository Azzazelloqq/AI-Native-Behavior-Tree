# Phase 7 integration gate status

Checkpoint: 2026-09-03, Unity `6000.5.8f1`. Gate executed against commit
`eedeb3c8408714ed5e5b3ee773a7a76c258e9864` (the `P7-015` commit).

## Verdict: Accepted, with disclosed gaps — does not declare `1.0.0`

Every step in `gate-runbook.md` ran against a clean detached snapshot. Full machine-readable results
are in `verification-results.json`. Summary:

- `P7-001` through `P7-015` all have real, accepted evidence — including 4 cards
  (`P6-012`, `P7-007`, `P7-010`, `P7-011`) whose own task-card `Status`/`Outcome` had never been
  updated to match their already-accepted evidence, a bookkeeping drift found and fixed by this
  gate's own review, not silently smoothed over;
- clean detached-UPM-harness compile: exit code 0;
- full detached-package EditMode: **1269/1270**, 0 skipped (up from `P6-GATE`'s 1224/1224) — **one
  real, disclosed, pre-existing failure**, root-caused to a genuine bug this gate's own harness
  technique was the first thing to ever surface (`McpApiReferenceGenerator`'s type-`<summary>`
  correlator silently no-ops for any real `file:`/registry UPM consumer), not fixed inside this gate
  per its own Forbidden-changes clause, spun off as `P7-021`;
- public API surface: **425 types, 2130 members — +13 types/+34 members versus `P6-GATE`'s own
  combined baseline (412/2096), confirmed purely additive** by direct type-set comparison (zero
  removals); every new type traces to an already-accepted Phase 7 card;
- assembly dependency audit: all 4 production `.asmdef` files byte-identical to `P6-GATE`'s own
  recorded references/platforms — zero drift, no new production assembly since Phase 6;
- `Documentation~/scope.md`'s 7-item "Release criteria for 1.0" checked individually against real
  evidence (`scope-release-criteria-checklist.md`): **5 fully met, 2 partially met** (stable
  contracts — blocked on tree-format v2 promotion; production-ready editor and debugger — blocked
  on a still-undecided-but-unbuilt production Play-mode host);
- `P7-001`'s public-API/persisted-format stability proposal, previously an open proposal with no
  recorded owner decision (a real, literal gap against this gate's own acceptance criteria), was
  decided live during this gate's own session — see `p7-001-stability-decision.md`. Three of its
  five open questions produced new required-before-1.0 follow-up cards (`P7-018` tree-format v2
  promotion, `P7-019` aggregate manifest schema, `P7-020` CI-enforced public-API diff), not left as
  prose;
- `P7-002`'s supported-platform matrix / regression-threshold proposal was already owner-accepted
  (2026-09-02) — reconfirmed still current, no change;
- `README.md`/`CHANGELOG.md` were found to have **no Phase 7 section at all** (both stopped at
  Phase 6) and were updated, checked against `claims-inventory.md` to confirm nothing stronger than
  verified evidence was introduced;
- `Documentation~/generated/migrations.md`'s own "MCP surface migrations" log was found to be
  inaccurate — a real, undocumented breaking change (`test-node`'s `scopeNote` field removal,
  shipped in `P7-009`) had never been logged — retroactively corrected as part of this gate's own
  documentation-consistency pass, live-verified (`McpDocumentationGeneratorsTests`, 11/11) in the
  host project before being folded into this gate's commit.

**Four gaps are explicitly disclosed, not smoothed over, each with a spun-off follow-up card:**

1. **Production Play-mode host does not exist.** `P7-010` decided its shape in full and proved it by
   spike (32,295 real Play-mode `Update()` calls, live debugger-attachment proof) — but no
   implementation card exists anywhere in Phase 7's own decomposition. The single most-repeated
   finding across this entire project (`P3-009`, `P3-010`, `P3-011`, `P6-008`, `P6-012`, now this
   gate) remains a decided-but-unbuilt design.
2. **Tree format `v2` (Agent/Shared blackboard) is not the production default.** The owner's own
   decision, recorded this gate, is that it should be — `P7-018`.
3. **The aggregate `get_project_manifest` response has no JSON Schema.** `P7-019`.
4. **The public-API dump is not CI-enforced**, and **`McpApiReferenceGenerator`'s summary-inlining
   silently breaks for any real UPM consumer** (this gate's own detached-harness regression is
   1269/1270, not 1270/1270, as a direct, honest consequence). `P7-020`, `P7-021`.

`AIBT.Editor` and `AIBT.Mcp` are explicitly kept experimental for `1.0` (not stable) — `AIBT.Mcp`
specifically because this gate found a real, previously-undocumented breaking change in its own
history (see above), which is itself evidence against declaring its external contract stable yet.

**Phase 7 is complete**: `P7-001` through `P7-016` are all `Done`. This gate does **not** declare
`1.0.0` — `Planning~/USER_ACTIONS.md`'s "Approve final public API and persisted-format stability
review" is a separate owner action, now materially informed by this gate's own findings and the
four spun-off follow-up cards (`P7-018` through `P7-021`), none of which are required for the gate's
own verdict but all of which are required before a clean `1.0.0` release.

## Gate package

| Document | Purpose |
| --- | --- |
| `contract-checklist.md` | Every Phase 7 card mapped to its own evidence, plus Phase 5/6 constraints re-checked |
| `p7-001-stability-decision.md` | The owner's actual, recorded decision on `P7-001`'s stability proposal |
| `scope-release-criteria-checklist.md` | `scope.md`'s 7 release criteria, checked item-by-item |
| `claims-inventory.md` | Exactly what Phase 7 claims and what it deliberately does not |
| `known-limitations.md` | Every disclosed gap, closed and still-open, carried into `1.0` planning |
| `gate-runbook.md` | The verification commands actually executed, and their actual results |
| `assembly-dependencies.json` | Per-asmdef reference audit against the forbidden-dependency list |
| `public-api.txt` / `.sha256` | Reflected public surface of all 4 assemblies at the candidate commit (additive-only versus `P6-GATE`) |
| `verification-results.json` | Machine-readable result of every gate-runbook step |
