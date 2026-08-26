# AI-Native Behavior Tree

**AIBT** is a deterministic behavior-tree authoring, compilation, and reference-execution package for Unity, designed for humans and AI agents.

The project targets a future Burst-compatible, zero-GC-per-tick production path; a readable visual editor; machine-readable contracts; reproducible performance research; and an optional MCP integration. DOTS Entities is not required.

Phase 2 adds the generated Burst dispatch and fixed-capacity native executor while preserving the Phase 1 semantic oracle. The initialized native paths have focused zero-GC evidence, Windows x64 and Android ARM64 each have an actual non-Development IL2CPP/Burst build, and the single-thread Web backend passes Chrome and Firefox conformance. Device performance, Safari/mobile Web, automatic policy selection, and any performance default or threshold are not claimed.

Phase 3 adds editor tooling on `UnityEditor.Experimental.GraphView`: a read-only graph adapter, deterministic auto-layout, manual organization and layout persistence, gated semantic editing, validation UX, reference-oracle-backed preview, read-only native debugger attachment, and a trace timeline view. Each is implemented and tested in isolation; none are yet wired into one live `Editor/Graph/` window.

Phase 4 measures scheduling and batching on real, non-Editor Players (Windows x64, Android ARM64, single-thread Web) and implements a calibrated work-estimation/batching model plus an `Auto` policy-selection heuristic. `Auto` currently underperforms the best fixed policy (`Immediate`/`Budgeted`/`BatchedJobsSameFrame`/`PipelinedJobs`) in most measured cases — reported honestly, not tuned away — and runtime autotuning was evaluated and rejected in favor of the static calibrated model. No performance default, regression threshold, or supported-hardware-class claim is adopted anywhere in the package.

> Status: Phases 1 through 4 are complete (`P2-025`, `P3-013`, and `P4-009` integration gates all accepted — see `Planning~/Evidence/`). MCP integration and hot reload remain later phases.

## Design goals

- Scale from a few agents to large populations without forcing one execution strategy.
- Keep semantic authoring, visual layout, and compiled runtime data separate.
- Make custom nodes safe to generate, inspect, validate, test, and compile.
- Give human and AI authors the same versioned node registry and validation rules.
- Preserve readable graphs through deterministic auto-layout, manual pinning, groups, comments, and reroutes.
- Measure scheduling and batching decisions on real platforms instead of relying on a magic agent threshold.

## Repository map

| Path | Responsibility |
| --- | --- |
| `Runtime/` | Compiled tree representation, execution state, scheduler, blackboard, commands |
| `Authoring/` | Semantic authoring model, validation, compilation contracts |
| `Editor/` | Graph adapter, layout, editing, validation, preview, and debugger components (implemented; not yet wired into one live window) |
| `CodeGen~/` | Generated Burst dispatch, analyzers, and templates |
| `Tools~/McpServer/` | Planned optional MCP server and AI-agent integration |
| `Schemas~/` | Draft schemas for canonical files and tool contracts |
| `Tests/` | Runtime and editor behavior tests |
| `Benchmarks~/` | Reproducible performance and platform research |
| `Documentation~/` | Architecture, scope, roadmap, and development rules |

## Architectural outline

```text
Visual Editor       MCP / AI tools       Text authoring
      \                   |                    /
       +----------- Authoring Model ----------+
                         |
                  Validate / Compile
                         |
                Immutable Runtime Program
                         |
       Reference oracle / Native generated executor
                         |
       Snapshot -> Execute -> Reduce -> Publish
```

The runtime does not depend on the editor, MCP, an LLM provider, or DOTS Entities. See [Architecture](Documentation~/architecture.md) and [Scope](Documentation~/scope.md).

## Data files

- `*.aibt.json` — canonical semantic tree.
- `*.aibt.layout.json` — shared presentation layout, groups, comments, and edge routing.
- `.aibt/policy.json` — project-specific validation and style rules.
- `*.aibtcase.json` — behavior scenarios used by simulation and tests.

Unity assets and runtime buffers are imported or compiled outputs, not the semantic source of truth.

## Development baseline

- Unity 6 (`6000.0` or newer); validated development baseline: `6000.5.8f1`.
- Burst `1.8.29`.
- Unity Collections `6.5.0`.
- MIT license.

Additional platforms and Unity versions are supported only after validation by the compatibility and benchmark matrix.

## Documentation

- [Scope](Documentation~/scope.md)
- [Architecture](Documentation~/architecture.md)
- [Execution and scheduling](Documentation~/execution-and-scheduling.md)
- [Data formats](Documentation~/data-formats.md)
- [Visual editor and layout](Documentation~/editor-and-layout.md)
- [AI and MCP integration](Documentation~/ai-and-mcp.md)
- [Hot reload](Documentation~/hot-reload.md)
- [Benchmarks](Documentation~/benchmarks.md)
- [Testing strategy](Documentation~/testing.md)
- [Roadmap](Documentation~/roadmap.md)
- [Architectural decisions](Documentation~/decisions.md)
- [Normative specifications](Documentation~/specifications/conventions.md)
- [Agent master plan](Planning~/MASTER_PLAN.md)
- [Machine-readable work items](Planning~/work-items.json)
- [User and infrastructure actions](Planning~/USER_ACTIONS.md)
- [Contributing](CONTRIBUTING.md)
