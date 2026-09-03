# P7-016 claims inventory

What Phase 7 claims, and what it deliberately does not — checked so `README.md`/`CHANGELOG.md`
never state anything stronger than the verified evidence.

## Claimed

- Real Unity Profiler instrumentation on every native/reference hot-path advance method, confirmed
  Burst-compiled (real disassembly, zero managed-fallback indicators), zero GC-allocation delta,
  disclosed +5.6% median wall-clock cost on a worst-case Editor-Mono micro-benchmark (`P7-003`).
- A long-running/stress test suite: 20,000-tick soak (zero GC alloc + no native-array resizing after
  warmup), 10,240-agent stress test, repeated-reload-under-load for both backends (`P7-004`).
- A real, working node-contract migration mechanism: declarative rename/add-with-default rules,
  in-memory-only application at `validate`/`compile`, an explicit MCP tool and Editor window for
  persisting a migration to disk (`P7-005`/`P7-006`).
- Real production trace wiring for `TryRunImmediate` (`P7-007`), a per-project leaf-registration
  mechanism closing the applied-node-discoverability gap (`P7-008`), and a generic native-dispatch
  translator with real `0..targetIndex` prefix support (`P7-009`).
- Native-backend hot reload, including active-instance migration (not idle-only, unlike the
  reference executor) — decided (`P7-011`) and implemented (`P7-012`), closing `P5-007`'s own
  remaining native-backend acceptance criteria in the same pass.
- Two new samples (`Samples~/CustomMcpToolProvider/`, `Samples~/FullExample/`, the latter trimmed to
  hot-reload-only, disclosed) and a generated, reflection-driven API reference covering 100% of
  public members' signatures across all 4 assemblies (`P7-013`/`P7-014`).
- Local-first release automation (readiness script + `workflow_dispatch` workflow), scoped around
  `P0-005`'s still-unresolved runner gap with an explicit, no-default acknowledgment input rather
  than silently skipping the Unity EditMode gate (`P7-015`).
- A recorded owner decision on public-API/persisted-format stability (`P7-001`'s proposal, decided
  live during this gate — `AIBT.Runtime`/`AIBT.Authoring` stable, `AIBT.Editor`/`AIBT.Mcp`
  experimental) and an accepted supported-platform matrix / regression-threshold proposal (`P7-002`).
- A clean detached-UPM-harness compile (exit 0) at the candidate commit.
- Zero drift in assembly dependency direction/references since `P6-GATE` (byte-identical
  `references`/`includePlatforms` on all 4 production asmdefs, confirmed by diff, not assumed).
- Two long-standing gaps closed since `P6-GATE`: trace inspection (`P7-007`) and applied-node
  discoverability (`P7-008`).

## Explicitly NOT claimed

- **`1.0.0` is not declared.** This gate confirms evidence completeness/consistency; the release
  decision remains the owner's own, per `Planning~/USER_ACTIONS.md`.
- **A production Play-mode host does not exist.** `P7-010` decided its shape; no implementation card
  exists in Phase 7. This is a real, load-bearing gap against `scope.md`'s "production-ready editor
  and debugger" criterion.
- **`AIBT.Editor` and `AIBT.Mcp` are not declared stable for `1.0`.** Both stay explicitly
  experimental — `AIBT.Mcp` because a real, undocumented output-shape change was found in its own
  history during this gate's review (see `known-limitations.md`); `AIBT.Editor` because no sample or
  extension point exercises it the way `Runtime`/`Authoring`/`Mcp` each have one.
- **The tree format's v2 (Agent/Shared blackboard) is not the default and remains reader-only in
  production.** The owner's decision is that it should become the default before `1.0` — spun off as
  `P7-018`, not built inside this gate.
- **The aggregate `get_project_manifest` response has no JSON Schema.** Spun off as `P7-019`.
- **The public-API dump is not CI-enforced.** Spun off as `P7-020`.
- **The full detached EditMode regression is not 100% green.** One real, pre-existing,
  disclosed failure (`McpApiReferenceGenerator`'s package-root resolution bug, `P7-021`) — not
  fixed inside this gate, not hidden from the record either.
- **`TryRunBudgeted`/`TryRunBatchedJobsSameFrame` do not have trace production wiring** — `P7-007`
  scoped to `TryRunImmediate` only, a real disclosed narrowing, not the full original gap.
- **No new performance default, regression threshold, or supported-hardware-class claim** is
  introduced by this gate itself, beyond what `P7-002`'s own already-accepted proposal states.
- **`AIBT.Mcp`'s external wire protocol (JSON-RPC-ish shape) is not independently certified stable**
  — only its C#-surface tool-name additivity was mechanically verified; deeper protocol-level
  guarantees were not audited beyond what this gate's own `p7-001-stability-decision.md` covers.
