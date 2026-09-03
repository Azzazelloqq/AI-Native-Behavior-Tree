# `Documentation~/scope.md`'s "Release criteria for 1.0" — checked item-by-item

Not assumed satisfied because Phase 7 completed — each bullet cites real evidence, or states
plainly why it is not fully met.

## 1. Stable runtime, node, tree, layout, policy, test-case, and trace contracts

**Partially met, one real gap disclosed.** `AIBT.Runtime`/`AIBT.Authoring` recommended stable for
`1.0` (owner-accepted this gate, `p7-001-stability-decision.md`) — additive-only since
`P2-GATE`/`P3-GATE`, zero removals/renames ever, mechanically confirmed across 6 prior gates. Layout
(`*.aibt.layout.json`), policy (`.aibt/policy.json`), and behavior-case (`*.aibtcase.json`) formats
are all `v1`, never changed post-acceptance (`P7-001`'s own table). Trace contract
(`NativeTraceEventKindV1`/channel shape) is part of `AIBT.Runtime`, unchanged since `P6-015`'s own
ADR. **Gap**: the tree format (`*.aibt.json`) still writer-defaults to `v1`; `v2` (Agent/Shared
blackboard) remains reader-only, gated off in production — the owner's decision (this gate) is that
`v2` should become the real default before this criterion is cleanly met (`P7-018`, not yet built).

## 2. Production-ready editor and debugger

**Not met — a real, disclosed gap.** `AIBT.Editor` is additive-only but explicitly kept
experimental (this gate's decision — no sample/extension point exercises it the way
`Runtime`/`Authoring` do). More directly: **no production Play-mode host exists** — `P7-010`
decided its shape and proved it via spike, but no implementation card exists anywhere in Phase 7's
own decomposition. The debugger (`NativeExecutionDebuggerSession`) and trace timeline
(`TraceTimelineWindow`) are real and tested, but only against self-driven or benchmark-driven
substitutes, never a genuine live-game Play-mode session — the single most-repeated disclosed gap
across this entire project (`P3-009`, `P3-010`, `P3-011`, `P6-008`, `P6-012`, and now this gate).

## 3. Verified zero-allocation Burst path and published benchmark methodology

**Met.** Zero-GC/lifetime gates proven at Phase 2 and reconfirmed at every native-path gate since;
`P7-003` added real `ProfilerMarker` instrumentation confirmed genuinely Burst-compiled (live
disassembly inspection, zero managed-fallback indicators) with a disclosed, honestly-measured
overhead (+5.6% median wall-clock on a worst-case Editor-Mono micro-benchmark, zero GC delta).
Benchmark methodology is published (`Documentation~/benchmarks.md`,
`Documentation~/compatibility-matrix.md`, `P7-002`'s own accepted regression-threshold proposal).

## 4. Automatic scheduler validated against fixed policies on supported platforms

**Met, with an honestly-disclosed result.** `P4-005`/`P4-006`/`P4-007` validated `Auto` against all
four fixed policies across real Windows x64/Android ARM64/Web Player evidence; `Auto` underperforms
the best fixed policy in most measured cases, reported honestly rather than tuned away, and runtime
autotuning was evaluated and rejected (`OQ-006`, `ADR-P4-007`) in favor of the static calibrated
model. "Validated" means measured and understood, not "wins" — that bar is met.

## 5. Hot reload with documented compatibility behavior

**Met, with disclosed scope limits.** Reference-executor hot reload (Phase 5, `ADR-P5-001`) and
native-backend hot reload (`P7-011`/`P7-012`) both exist, both documented, both tested live. Disclosed
limits: reference-executor migration is idle-instance-only (falls back to full restart for a
genuinely active instance); native migration is *not* idle-only (a real, positive difference,
proven live against an active instance). No regression threshold or "acceptable reload cost" claim
is adopted (deliberately, per `USER_ACTIONS.md`).

## 6. MCP server and custom tool extension contract

**Met.** A real, running MCP server (`MCP~/Server/` + `AIBT.Mcp`) with fail-closed permission
enforcement across 8 categories, a full authoring/verification/test/benchmark/node-development tool
surface, and a real IoC custom-tool-provider extension point (`P6-010`, live-verified this gate's
own `P7-013` samples work). `AIBT.Mcp`'s own *stability* is separately flagged experimental (see
criterion 1) — the contract existing and working is a different bar than declaring it frozen, and
this criterion is about the former.

## 7. Complete tests, samples, API documentation, migration tooling, and supported-platform matrix

**Mostly met, two small disclosed gaps.** Tests: see `verification-results.json` for the exact
detached-harness count (up from `P6-GATE`'s 1224). Samples: `P7-013` added two real, live-verified
samples (one intentionally trimmed in scope, disclosed). API documentation: `P7-014`'s generated
reference covers 100% of public member signatures across all 4 assemblies — but this gate found its
type-`<summary>` inlining silently no-ops for any real UPM (`file:`) consumer (`P7-021`, not yet
fixed). Migration tooling: `P7-005`/`P7-006` built the real mechanism; this gate found and fixed one
real undocumented entry in its own MCP-surface migration log (`test-node`'s `scopeNote` removal).
Supported-platform matrix: `P7-002`'s `compatibility-matrix.md`, owner-accepted 2026-09-02.
