# P7-026 — Validate Player build size does not scale materially with tree count

Status: `Done`

## Objective

Owner request: confirm, with a real measurement, that shipping many authored `.aibt.json` trees does
not meaningfully grow the Player build. This is an architectural claim AIBT's own design implies but
has never actually measured: `AGENTS.md` states "Authoring data, presentation layout, and compiled
runtime data are separate models," and `ADR-P7-010`'s own reasoning notes a shipped game needs only
`AIBT.Runtime` plus an already-compiled program — trees are meant to be **data**, compiled once per
distinct node-type catalog (`Authoring/Registry/Generated/` codegen), not per authored tree. If true,
build size should scale with the number of distinct node *types* used (bounded, fixed) and with raw
tree JSON/compiled-program payload size (small, linear, expected), not blow up combinatorially with
tree *count*. This has never been checked against a real Player build.

Existing precedent to reuse, not reinvent: `Benchmarks~/Phase4/Platform/{Windows,Android,Web}/
Results/` already contain real committed Player build artifacts and their measured sizes
(`P4-008`), and `P7-017`'s own evidence explains the established `.gitignore`/committed-evidence
convention for this kind of binary. This card is a *different* concern from `P7-017`'s (which is
about git-repo evidence-artifact hygiene, not runtime build-size scaling) — confirmed with the owner
this session that `P7-017` is not what was meant.

## Depends on

- `P4-008` (existing real platform Player builds — the baseline to diff against, not a fresh
  from-scratch build).
- `P7-002` (`compatibility-matrix.md` — where a build-size claim, once measured, should be recorded
  as a citable fact, matching every other platform claim's own discipline).

## Required reading

- `Benchmarks~/Phase4/Platform/README.md` and `{Windows,Android,Web}/Results/` (the existing
  real-build measurement precedent and its own recorded baseline sizes).
- `Documentation~/architecture.md`'s data-ownership rows (the claim being tested: authoring/compiled
  data separation).
- `Authoring/Registry/Generated/` and the codegen pipeline (`CodeGen~/`) — confirm what code
  generation is actually keyed on (node type catalog vs. individual tree), since that determines
  whether the architectural claim is even structurally true before measuring.

## Allowed changes

- A new benchmark under `Benchmarks~/Phase7/` (or wherever this session's own Phase 7 benchmark
  precedent lives, matching `P7-003`'s own `Benchmarks~/Phase7/Profiler/`): build the same Player
  target twice — once with the existing tree/fixture count, once with a synthetic multiplier (e.g.
  50x or 100x copies of an existing tree, same node types reused, no new node types) — and diff the
  resulting build size.
- `Documentation~/compatibility-matrix.md` — a new row/section recording the measured result, once
  real, citing the exact build artifacts.
- `Planning~/Evidence/P7-026/`.

## Forbidden changes

- Do not assume the architectural claim is true and skip measuring it — if the measurement finds
  build size *does* scale materially with tree count, that is a real, disclosable finding (and
  possibly a new follow-up card), not something to quietly avoid reporting.
- Do not invent new node types for the synthetic multiplier — the whole point is isolating tree
  *count* from node-*type* count, which is the actual, already-suspected cost driver.

## Deliverables

- A real, reproducible before/after Player build-size comparison at a real multiplier of tree count,
  same node types.
- A clear, evidence-backed statement of whether the "build size scales with node types, not tree
  count" claim holds — and if it doesn't, by how much and why.

## Acceptance criteria

- The comparison uses two real Player builds (not an estimate), and the delta is attributed to a
  specific, understood mechanism (e.g. serialized tree JSON payload size only, vs. any unexpected
  per-tree code generation).

## Required verification

```text
Verify-Static.ps1
two real Player builds, real file-size diff, root-caused
```

## Handoff notes

- Completed 2026-09-04: two real Windows x64 IL2CPP release builds, 1/100 Resources trees,
  both Player validation probes passed. Raw shipped delta +36,028 bytes; code binaries and
  IL2CPP metadata byte-identical. Growth is serialized tree data and resource index metadata.
  Static verification passed (7 schemas, 137 work items). See
  [evidence](../../Evidence/P7-026/README.md) and
  [harness](../../../Benchmarks~/Phase7/BuildSize/README.md).
  Authored-JSON packaging includes a fixed Authoring/compiler probe; runtime-only/precompiled,
  other-platform and catalog-size scaling claims remain unmeasured.

- 2026-09-04: owner authorized step 1 of `Planning~/NEXT_STEPS.md`. Implementation uses the
  established isolated Windows IL2CPP harness pattern, with two current-source builds (1 and 100
  trees), fixed node catalog, BuildReport attribution and actual Player payload validation.
  Historical P4 scheduling build sizes are context, not a controlled one-tree baseline.

- Owner request this session (2026-09-03), explicitly distinguished from `P7-017` (repo
  evidence-artifact size, a different concern) after the owner clarified they meant runtime build
  size scaling with authored tree count. Confirmed in scope for `1.0`.
