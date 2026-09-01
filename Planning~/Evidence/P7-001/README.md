# P7-001 public API and persisted-format stability inventory evidence

## Result

Done. Produces the concrete material `Planning~/USER_ACTIONS.md`'s "Approve final public API and
persisted-format stability review" bullet requires -- a proposal, not a freeze (freezing a public
contract is the owner's own decision, per `Planning~/DECISION_BOUNDARIES.md`). No production file
was touched, per this card's own Forbidden-changes clause.

## What was built

**`Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` + `PublicApiDump.cs.txt`** (new, permanent):
widens `Tools~/Verification/P2/Audit/`'s own committed dump script from 2 assemblies
(`AIBT.Runtime`/`AIBT.Authoring`) to all 4 (`AIBT.Runtime`/`AIBT.Authoring`/`AIBT.Editor`/`AIBT.Mcp`).
Every gate since `P3-GATE` actually ran an *ad-hoc, uncommitted* extended copy of this same script to
cover the assemblies added after Phase 2, then discarded the extension each time
(`Planning~/Evidence/P6-GATE/gate-runbook.md:83-114`) -- the committed script was therefore
permanently stale. This is that extension made durable: same isolated-project-plus-headless-Unity
mechanism (Windows PowerShell 5.1 cannot reliably reflect Unity-Mono-compiled assemblies itself),
same byte-compatible `ASSEMBLY`/`TYPE`/`FIELD`/`METHOD`/`PROPERTY` line format so the output is
directly diffable against all five prior gates' own `public-api.txt` files with no translation. No
new package dependency was needed -- confirmed by reading `AIBT.Editor.asmdef`/`AIBT.Mcp.asmdef`
directly, both reference only `AIBT.Runtime`/`AIBT.Authoring`/`Newtonsoft.Json`/`Unity.Collections`,
already in the isolated project's manifest template.

Run live against current `main`: **4 assemblies, 417 types, 2111 members**
(`Planning~/Evidence/P7-001/public-api-current.txt` + `.sha256`).

**`stability-review-proposal.md`** (new): the actual owner-facing deliverable -- per-assembly
summary, persisted-format table, and the explicit open questions the owner must answer.

## Classification methodology (mechanical, not memory)

Every one of the current dump's 2532 lines was classified by set comparison (`comm`/`sort -u`)
against all five prior gate dumps (`Planning~/Evidence/{P2,P3,P4,P5,P6}-GATE/public-api.txt`, plus
`P6-GATE/public-api-mcp.txt`), not read by hand or recalled from memory -- satisfying this card's own
acceptance criterion directly:

| First appeared at | New lines | Running total |
|---|---:|---:|
| `P2-GATE` (baseline) | 2168 | 2168 |
| `P3-GATE` | 211 | 2379 |
| `P4-GATE` | 0 | 2379 |
| `P5-GATE` | 39 | 2418 |
| `P6-GATE` (incl. `public-api-mcp.txt`) | 89 | 2507 |
| Since `P6-GATE` (current) | 25 | 2532 |

Totals reconcile exactly (2168+211+0+39+89+25 = 2532 = the current dump's own line count) -- no
double-counting, no silent omission. **Zero lines present in any prior gate's dump are absent from
the current one** -- every gate's own "purely additive" claim is empirically confirmed across the
*entire* P2-through-current history in one pass, for the first time (each gate only ever checked its
own diff against its immediate predecessor). The 25 lines new since `P6-GATE`
(`Planning~/Evidence/P7-001/new-since-p6-gate.txt`) trace entirely to `P7-008`'s own public surface
(`IReferenceLeafBehavior`/`ReferenceLeafContext`/`IReferenceLeafBehaviorProvider`/
`NodeRegistryBuilder.AddProjectExtension`) -- `P7-007`/`P7-009`/`P7-012`/`P7-004`'s own new code is
entirely `internal` or test-only, correctly contributing nothing here, which is itself a consistency
check that the dump/diff pipeline is accurate.

## Persisted-format schema conformance -- closing a real gap found in `Verify-Schemas.ps1`

Reading `Tools~/Verification/Verify-Schemas.ps1` directly found it validates all six schema files are
structurally well-formed JSON Schema (`--check-metaschema`), but only ever validates a **real
document** against two of the six (`work-item-index.schema.json` ↔ `work-items.json`,
`policy.schema.json` ↔ `.aibt/policy.json`). The other four had never been checked against a real
shipped example. Closed here, live, with the same `check-jsonschema==0.38.0` tool via `uvx`:

| Schema | Real example validated | Result |
|---|---|---|
| `tree.schema.json` | `Tests/Fixtures/Trees/Serialization/valid-minimal.aibt.json` | pass |
| `layout.schema.json` | `Tests/Editor/Layout/Fixtures/mixed.expected.aibt.layout.json` | pass |
| `behavior-case.schema.json` | `Tests/Fixtures/Cases/schema-valid-minimal.aibtcase.json` | pass |
| `node-manifest.schema.json` | `example-node-manifest.json` (generated live, see below) | pass |
| `policy.schema.json` | `.aibt/policy.json` (already covered by `Verify-Schemas.ps1`) | pass |
| `work-item-index.schema.json` | `Planning~/work-items.json` (already covered) | pass |

**A real, disclosed finding along the way**: `node-manifest.schema.json` governs *one node's own*
manifest document (`typeId`/`summary`/`category`/`kind`/... -- matching
`NodeManifestCanonicalJson.ToJson(NodeManifest)`'s own single-node output), not the aggregate
`{"format":"aibt-project-manifest",...}` document the `get_project_manifest` MCP tool returns. First
attempt validated the wrong document shape and failed with 19 "required property" errors -- a real,
useful signal that the *aggregate* project-manifest document has no schema of its own at all (see
open question 4 in the proposal). Fixed by generating a genuine single-node example instead, live via
the real `AIBT.Mcp.McpToolDispatcher.Dispatch("get_node_contract", ...)` entry point (temporary
script added outside `AIBT/`, deleted after running, matching `P7-008`/`P7-009`'s own established
live-verification pattern) for `aibt.core.memory-sequence` -- validates cleanly.

## Schema shape-change history

Checked every schema file's own git log directly (not assumed): `tree.schema.json` gained
format-version-2 support (`blackboardContracts`, extended blackboard bindings) in commit `360bbe7`,
and `layout.schema.json` had one reconciliation fix in commit `57a2487` (`fix(p3-002)`) -- both,
confirmed by comparing commit timestamps against their owning gates' own evidence-file timestamps,
landed **before** their respective gate's acceptance (`P2-GATE` at `2026-08-18 18:04:45`, tree fix at
`09:55:04` same day; `P3-GATE` evidence at `2026-08-19`, layout fix at `2026-08-18 22:58:35`). **No
schema has changed shape after the gate that accepted it** -- a clean, positive finding for the
stability review, not assumed.

## Verification

```text
Verify-Static.ps1 -- passed
Get-FullPublicApi.ps1 run live (Unity headless, isolated project) -- AIBT_PUBLIC_API_OK|assemblies=4|
  types=417|members=2111|sha256=296e48e5...
Diff-based classification against all 5 prior gate dumps -- reconciles exactly, zero omissions,
  zero removals
check-jsonschema==0.38.0 (uvx) run live against a real example document for all 6 schemas -- 6/6 pass
```

## Scope and limitations

- This card does not freeze anything and adds no `[Obsolete]` attribute or rename -- per its own
  Forbidden-changes clause, `stability-review-proposal.md`'s open questions are for the owner.
- Schema shape-change history was checked via git log + timestamp comparison, not a byte-level diff
  of every historical schema revision against every historical gate acceptance commit -- sufficient
  to answer "did anything change post-acceptance" (no), not a full schema changelog.
- `Get-FullPublicApi.ps1` was run once, live, against a clean current `main` checkout state at
  the time of this card; like every prior gate's own dump, it is a point-in-time snapshot, not a
  continuously-enforced check (no CI job runs it automatically yet -- that would be `P7-015`'s own
  scope if assigned).
