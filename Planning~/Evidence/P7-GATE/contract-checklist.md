# P7-016 contract checklist

Every Phase 7 card mapped to its own real evidence, plus the bookkeeping-drift finding this gate
fixed (`P6-012`, `P7-007`, `P7-010`, `P7-011` — real, accepted evidence existed, but the task-card
`Status`/`Outcome` fields were never updated to match; all 4 fixed during this gate's own review,
see `Planning~/Tasks/P6/P6-012-phase6-integration-gate.md` / `Planning~/Tasks/P7/P7-007-*.md` /
`P7-010-*.md` / `P7-011-*.md`).

| Card | Title | Own contract | Evidence |
|---|---|---|---|
| `P7-001` | Public API / persisted-format stability inventory | Assemble real evidence for the owner's own required stability decision, not decide it | `Planning~/Evidence/P7-001/` — recorded owner decision now in `p7-001-stability-decision.md` (this gate) |
| `P7-002` | Supported-platform matrix / regression-threshold proposal | Same, for platform/threshold policy | `Planning~/Evidence/P7-002/` — owner approved as-is, 2026-09-02, `Documentation~/compatibility-matrix.md` ACCEPTED |
| `P7-003` | Profiler integration and validation | `ProfilerMarker`s on native/reference hot paths, real Player capture | `Planning~/Evidence/P7-003/` |
| `P7-004` | Long-running and stress test suite | Soak/stress/repeated-reload tests, both backends | `Planning~/Evidence/P7-004/` |
| `P7-005` | Migration-tooling design decision | `ADR-P7-005` (`AIBT-036`), spike-proven | `Planning~/Evidence/P7-005/` |
| `P7-006` | Migration tooling implementation | Real declarative rule engine + MCP tool + Editor window, applying `ADR-P7-005` | `Planning~/Evidence/P7-006/` |
| `P7-007` | Native trace production-wiring implementation | Real external recorder wired into `SchedulingPolicyDriver`, applying `ADR-P6-015` | `Planning~/Evidence/P7-007/` |
| `P7-008` | Per-project leaf-registration implementation | Public leaf-extension surface + MCP discovery wiring, applying `ADR-P6-017` | `Planning~/Evidence/P7-008/` |
| `P7-009` | Generic native-dispatch translator implementation | Real `0..targetIndex` dispatch translator, applying `ADR-P6-022`; widened `test-node` | `Planning~/Evidence/P7-009/` |
| `P7-010` | Production Play-mode host decision | `ADR-P7-010` (`AIBT-034`), spike-proven; **no implementation card exists yet** | `Planning~/Evidence/P7-010/` |
| `P7-011` | Native-backend hot reload decision | `ADR-P7-011` (`AIBT-035`), spike-proven | `Planning~/Evidence/P7-011/` |
| `P7-012` | Native-backend hot reload implementation | Real classifier/migration, applying `ADR-P7-011`; also closed `P5-007`'s own remaining gap | `Planning~/Evidence/P7-012/` |
| `P7-013` | Samples expansion | `Samples~/CustomMcpToolProvider/`, `Samples~/FullExample/` (hot-reload-only, disclosed trim) | `Planning~/Evidence/P7-013/` |
| `P7-014` | Generated C# API reference documentation | Real reflection-driven generator, all 4 assemblies, 100% signature coverage | `Planning~/Evidence/P7-014/` |
| `P7-015` | Release automation | Local-first readiness script + `workflow_dispatch` release workflow, scoped around `P0-005` | `Planning~/Evidence/P7-015/` |

`P7-017` (evidence-artifact size discipline) and the 3 new cards this gate spun off (`P7-018`
tree-format v2 promotion, `P7-019` manifest schema, `P7-020` CI-enforced API diff) are independently
assignable, **not required for this gate's own verdict** — matching `P6-012`'s own precedent of
deferring cross-cutting/required-before-1.0 work to dedicated follow-up cards rather than folding it
into the gate task.

## Phase 5/6 constraints Phase 7 must not violate

Re-checked, not assumed still true:

- `AIBT.Runtime`/`AIBT.Authoring`/`AIBT.Editor` byte-identical `references`/`includePlatforms` to
  `P6-GATE`'s own recorded baseline — confirmed by direct diff, zero drift (see
  `assembly-dependencies.json`).
- No new production assembly added since Phase 6 — `AIBT.Mcp` remains the newest, unchanged since
  `P6-005`.
- No production Runtime/Authoring/Editor code depends on `UnityMCP`, `Unity.Entities`, `OpenAI`, or
  `Anthropic` — reconfirmed by grep against the clean clone.
- Hot reload's own disclosed native-backend gap (`P5-004`/`P5-007`) is now closed — `P7-012`.
- `P6-012`'s own two disclosed exit-criterion gaps (trace inspection, applied-node discoverability)
  are both closed — `P7-007`, `P7-008`.
