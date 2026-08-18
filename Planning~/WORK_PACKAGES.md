# Work packages

Phases 0, 1, and 2 are decomposed into atomic cards. Later packages remain non-assignable until their entry gate is complete and a dedicated decomposition task is approved.

## P0 — toolchain and evidence

Purpose: prove the repository can be built and tested on the declared baseline, then close mandatory platform and CI evidence. P0-001 and P0-002 gate implementation; the representative Web evidence waits for P1-018 and closes before the final P1 integration gate.

Atomic cards: `P0-001` through `P0-006`.

Exit: all Phase 0 criteria in `MASTER_PLAN.md` pass with recorded evidence. Phase numbering does not imply that all P0 evidence precedes the P1 reference slice; `work-items.json` is authoritative.

## P1 — semantic vertical slice

Purpose: create one correct, deterministic, backend-neutral reference path from canonical JSON to observable behavior.

Atomic cards: `P1-001` through `P1-019`.

Excluded: Burst code generation, parallel jobs, shared blackboard execution, production editor UI, MCP, hot reload, and performance autotuning.

Exit: all Phase 1 criteria in `MASTER_PLAN.md` pass and independent integration review accepts the slice.

## P2 — data-oriented Burst runtime

Entry: the P1 semantic implementation and golden slice are accepted, and its behavior cases are immutable regression inputs. On 2026-08-14 the owner explicitly authorized P2 decomposition and the P2-001 contract task without waiting for the external self-hosted Unity runner; this waiver does not convert `P0-005`, `P0-006`, or `P1-019` to Done.

Atomic cards: `P2-001` through `P2-025`.

Planned outputs:

- exact public Burst node API and generated dispatch;
- native compiled-program binding and state arenas;
- agent scope and deterministic shared reductions;
- snapshot/command integration API;
- parallel native executor and allocation tests;
- Phase 2 benchmark baselines.

The atomic dependency graph is recorded in `work-items.json`; agents may not implement P2 directly from this summary.

`P2-001` is the mandatory normative public node-ABI contract and analyzer feasibility task. No production custom user-node API or native dispatch may be implemented before that contract is independently accepted.

Exit: representative public custom nodes execute through generated dispatch in fixed native Immediate, deterministic Budgeted, and BatchedJobsSameFrame paths; every applicable P1 behavior case remains equivalent; the measured initialized Burst path has zero managed allocations; Windows Player, Android ARM64 AOT build, and accepted desktop Web policies have bounded evidence; `P2-025` independently accepts the slice.

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
