# Work packages

Only Phase 0 and Phase 1 are decomposed into assignable atomic cards. Later packages remain non-assignable until their entry gate is complete and a dedicated decomposition task is approved.

## P0 — toolchain and evidence

Purpose: prove the repository can be built and tested on the declared baseline before implementation begins.

Atomic cards: `P0-001` through `P0-006`.

Exit: all Phase 0 criteria in `MASTER_PLAN.md` pass with recorded evidence.

## P1 — semantic vertical slice

Purpose: create one correct, deterministic, backend-neutral reference path from canonical JSON to observable behavior.

Atomic cards: `P1-001` through `P1-019`.

Excluded: Burst code generation, parallel jobs, shared blackboard execution, production editor UI, MCP, hot reload, and performance autotuning.

Exit: all Phase 1 criteria in `MASTER_PLAN.md` pass and independent integration review accepts the slice.

## P2 — data-oriented Burst runtime

Entry: P1 is done and its behavior cases are immutable regression inputs.

Planned outputs:

- exact public Burst node API and generated dispatch;
- native compiled-program binding and state arenas;
- agent scope and deterministic shared reductions;
- snapshot/command integration API;
- parallel native executor and allocation tests;
- Phase 2 benchmark baselines.

Before assignment, a coordinator MUST create atomic cards from measured P1 layouts and the accepted Web spike. Agents may not implement P2 directly from this summary.

The first P2 card MUST be a normative public node-ABI contract and analyzer feasibility task. No custom user-node API may be implemented before that contract is accepted.

## P3 — editor and layout

Entry: authoring/compiler APIs are stable enough for an adapter and the graph-framework spike is accepted.

Planned outputs: semantic graph editing, deterministic layout service, manual organization, validation UX, debugger, trace views, and large-graph interaction tests.

## P4 — scheduler research

Entry: native executor and benchmark harness exist.

Planned outputs: scenario matrix, raw-result format, Windows/Android/Web profiles, calibrated fixed policies, explainable Auto policy, and an evidence-based autotuning decision.

## P5 — hot reload

Entry: runtime memory layout and editor/compiler revisions are stable.

Planned outputs: safe restart, compatibility classifier, affected-subtree restart, compatible state migration, async cancellation, traces, and tests.

## P6 — AI and MCP

Entry: authoring, validation, diagnostics, behavior cases, trace, and node registry are stable public contracts.

Planned outputs: transactional MCP server, permissions, discovery, mutation, verification, node generation, custom tools, and protocol conformance tests.

## P7 — production hardening

Entry: product feature scope is implemented.

Planned outputs: compatibility matrix, platform CI, stress/soak tests, documentation and samples, migrations, package validation, release automation, and 1.0 contract review.
