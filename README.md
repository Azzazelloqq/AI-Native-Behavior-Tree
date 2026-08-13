# AI-Native Behavior Tree

**AIBT** is a deterministic behavior-tree authoring, compilation, and reference-execution package for Unity, designed for humans and AI agents.

The project targets a future Burst-compatible, zero-GC-per-tick production path; a readable visual editor; machine-readable contracts; reproducible performance research; and an optional MCP integration. DOTS Entities is not required.

Phase 1 validates the managed reference backend in Unity Editor on Windows x64, an Android ARM64 IL2CPP + Burst build smoke, and single-thread Unity Web execution in Chrome and Firefox. These results do not claim a production Burst/jobs executor, device performance, or zero-allocation execution.

> Status: Phase 1 semantic vertical slice implemented. Canonical authoring, validation, compilation, immutable runtime programs, the deterministic reference executor, blackboard/observers, async commands, step budgeting, and backend-neutral behavior cases are covered by golden integration tests. The visual editor, MCP integration, native jobs executor, hot reload, and production performance work remain later phases.

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
| `Editor/` | Planned visual editor, debugging, profiling, and layout authoring |
| `CodeGen~/` | Planned generated dispatch, analyzers, and templates |
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
           Reference Immediate / Step-Budgeted
                         |
              World Snapshot -> Command Buffer
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
- [Benchmarks](Documentation~/benchmarks.md)
- [Testing strategy](Documentation~/testing.md)
- [Roadmap](Documentation~/roadmap.md)
- [Architectural decisions](Documentation~/decisions.md)
- [Normative specifications](Documentation~/specifications/conventions.md)
- [Agent master plan](Planning~/MASTER_PLAN.md)
- [Machine-readable work items](Planning~/work-items.json)
- [Safe parallelization waves](Planning~/PARALLELIZATION.md)
- [User and infrastructure actions](Planning~/USER_ACTIONS.md)
- [Contributing](CONTRIBUTING.md)
