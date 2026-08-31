# P7-001 — Public API and persisted-format stability inventory

Status: `Draft`

## Objective

Prepare the concrete material `Planning~/USER_ACTIONS.md`'s "Approve final public API and
persisted-format stability review" requires, so the owner can actually make that decision instead
of being asked to approve an abstraction. Inventory every public C# surface (`AIBT.Runtime`,
`AIBT.Authoring`, `AIBT.Editor`, `AIBT.Mcp`) and every persisted format (`*.aibt.json`,
`*.aibt.layout.json`, `*.aibtcase.json`, `.aibt/policy.json`, node manifest/registry JSON, the
generated descriptor JSON) against its own growth history across every accepted gate
(`P2-GATE`/`P3-GATE`/`P4-GATE`/`P5-GATE`/`P6-GATE`'s own `public-api.txt` dumps), and flag anything
that looks unintentionally exposed, still churning, or under-specified for a `1.0.0` freeze.

This card does not freeze anything itself — freezing a public contract is an owner decision per
`Planning~/DECISION_BOUNDARIES.md`'s "public or cross-assembly API shape" escalation rule. It
produces the proposal.

## Depends on

- `P6-012` (Phase 6 integration gate; the last point every prior gate's `public-api.txt` was
  captured against).

## Required reading

- Every accepted gate's own evidence: `Planning~/Evidence/{P2,P3,P4,P5,P6}-GATE/public-api.txt` and
  their own "purely additive" diff claims.
- `Documentation~/scope.md`'s "Release criteria for 1.0" (stable runtime/node/tree/layout/policy/
  test-case/trace contracts).
- `Documentation~/architecture.md`'s dependency-direction rules (a stability review must confirm
  they still hold, not just that the surface grew additively).
- Every `Documentation~/specifications/*.md` normative contract version already shipped.

## Allowed changes

- `Planning~/Evidence/P7-001/` (the inventory and proposal).
- A read-only public-API dump script under `Tools~/Verification/P7/` if none of the existing
  per-gate scripts are reusable as-is (check before writing a new one).

## Forbidden changes

- Any production file. This card reads and reports; it does not change a public contract, add an
  `[Obsolete]` attribute, or rename anything.
- Deciding the freeze itself. The deliverable is a proposal with explicit open questions for the
  owner, not a unilateral "these are now stable" claim.

## Deliverables

- A single consolidated public-API surface dump (current `main`, all four assemblies) with a
  category per member: unchanged since first introduction, grown additively, or genuinely still
  churning (cite the commit/gate where it last changed and why).
- A persisted-format inventory: for each format, its current schema version, whether any accepted
  decision already commits to a compatibility/migration story for it (cross-reference `P7-005`'s
  own scope once decomposed), and whether it has ever changed shape post-acceptance.
- A short list of concrete open questions the owner must answer to approve a `1.0.0` freeze (e.g.,
  "is `AIBT.Mcp`'s 7-type surface considered stable-for-1.0 or explicitly experimental past 1.0
  given it depends on the external `dotnet` process model").

## Acceptance criteria

- Every public type/member in all four assemblies is accounted for exactly once (no silent
  omission), cross-checked against the real current `public-api.txt`-style dump, not assumed from
  memory of prior gates.
- Every persisted format's schema file (where one exists under `Schemas~/`) is checked against its
  own currently-shipped writer/reader for actual conformance, not just cited.
- The proposal explicitly separates "recommended stable" from "recommended still-experimental"
  rather than blanket-freezing everything for convenience.

## Required verification

```text
Verify-Static.ps1
fresh public-API dump for all four assemblies, diffed against every prior gate's own public-api.txt
schema-vs-writer/reader conformance check for every Schemas~/*.schema.json file
```

## Handoff notes

- `P7-016` (the Phase 7 integration gate) cites this card's proposal, plus the owner's actual
  decision on it, as one of its own acceptance criteria — the gate cannot itself decide the freeze
  either.
