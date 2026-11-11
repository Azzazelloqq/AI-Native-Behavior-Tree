# AI-Native Behavior Tree

**AIBT** is a deterministic behavior-tree authoring, compilation, and reference-execution package for Unity, designed for humans and AI agents.

Command-driven agents: [CLI and MCP setup](Documentation~/cli.md). Both adapters reuse the same
Editor operations; CLI can start/recover the bridge in an already-open project without a manual
Start click. MCP remains available for protocol clients and custom project tools.

Agents can inspect operation schemas, edit/validate project-native trees, prepare a catalog-pinned
runtime asset and inspect a specific host's bounded native trace. Reference simulation remains a
separate backend. Expected-outcome node cases report behavior proof separately from dispatch.

The project targets a future Burst-compatible, zero-GC-per-tick production path; a readable visual editor; machine-readable contracts; reproducible performance research; and an optional MCP integration. DOTS Entities is not required.

Phase 2 adds the generated Burst dispatch and fixed-capacity native executor while preserving the Phase 1 semantic oracle. The initialized native paths have focused zero-GC evidence, Windows x64 and Android ARM64 each have an actual non-Development IL2CPP/Burst build, and the single-thread Web backend passes Chrome and Firefox conformance. Device performance, Safari/mobile Web, automatic policy selection, and any performance default or threshold are not claimed.

Phase 3 adds editor tooling on `UnityEditor.Experimental.GraphView`, including graph rendering, layout, semantic editing operations, validation, reference preview and native tracing. The Graph Editor now exposes initial tree editing and draft saving; preview, live debugging, project registry integration and parts of layout organization remain separate or incomplete. The editor is experimental.

Phase 4 measures scheduling and batching on real, non-Editor Players (Windows x64, Android ARM64, single-thread Web) and implements a calibrated work-estimation/batching model plus an `Auto` policy-selection heuristic. `Auto` currently underperforms the best fixed policy (`Immediate`/`Budgeted`/`BatchedJobsSameFrame`/`PipelinedJobs`) in most measured cases — reported honestly, not tuned away — and runtime autotuning was evaluated and rejected in favor of the static calibrated model. No performance default, regression threshold, or supported-hardware-class claim is adopted anywhere in the package.

Phase 5 adds hot reload for the reference-executor backend: a resolved compatibility model (construct-fresh-and-selectively-copy by stable authoring node ID, never in-place mutation), full restart, localized subtree restart, idle-instance compatible state migration, and an explicit, explained Editor workflow. Measured evidence shows compatible migration costs roughly half of a full restart at the same tree size, on both Editor and a real Windows Player. Native-backend hot reload does not exist yet, and migration is scoped to an idle old instance (a genuinely active one falls back to full restart) — both disclosed, not silently assumed.

Phase 6 adds a real, working MCP integration: a standalone `dotnet` MCP server (`MCP~/Server/`) bridged to the Unity Editor (`AIBT.Mcp`), with discovery, authoring, validation/compilation/simulation, test/benchmark, and custom-node-generation tools, a fail-closed permission model, custom MCP tool providers a consuming project can register via inversion of control, and generated agent documentation (node catalog, workflow guide, recipes, anti-patterns). Every tool is live-verified against a real MCP client (the official `@modelcontextprotocol/inspector` CLI), including generating, compiling, testing, and applying a genuinely new custom node end-to-end. Its original trace and project-node-discovery gaps were addressed in later Phase 7 work; see the production-host and editor limitations below for the current integration boundaries.

Phase 7 adds native tracing, project leaf registration, profiling, stress tests, contract migration, native hot reload, samples and release tooling. A production Play-mode host and world integration were implemented after the original integration gate; see [Production tree host](Documentation~/production-host.md) and the Swarm Arena sample. The editor and MCP remain experimental, the Graph Editor creates `v2` trees while preserving existing `v1` trees, and the package has not declared 1.0.

> Status: Phases 1 through 7 are complete (`P2-025`, `P3-013`, `P4-009`, `P5-010`, `P6-012`, and `P7-016` integration gates all accepted — see `Planning~/Evidence/`). `1.0.0` has not been declared; that remains a separate owner decision per `Planning~/USER_ACTIONS.md`.

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
| `Editor/` | Experimental graph authoring, layout, validation, preview, and debugger components |
| `CodeGen~/` | Generated Burst dispatch, analyzers, and templates |
| `MCP/` | Unity-side MCP bridge (Editor-only): discovery, authoring, verification, node-development, and custom-tool-provider support |
| `MCP~/Server/` | Standalone MCP server (external `dotnet` process) |
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

Start with [First production agent](Documentation~/consumer-quickstart.md): the **First Agent**
sample prepares a runtime asset in the Editor, runs a generated Float32-configured Action without
Authoring in its gameplay assembly, and connects live tracing to the source graph.

The graph now provides **Prepare Runtime**, **Preview** and **Debug Selected**. Dragging ports
connects/reparents children in explicit append order; moving nodes never changes execution order.
Shared preview/trace annotations preserve the authoring graph's layout and detach on source edits.
Native-only project nodes still require reference providers for preview.

### Graph Editor (experimental)

Open **AIBT > Graph Editor**. **New** creates an unsaved version 2 tree with a sequence root; select a node
and use **Add Child** to choose a registered built-in node. Select a node to edit its supported
parameters in the right-hand panel. **Earlier** and **Later** change semantic child order; the
number shown on each child reflects that order, independently of its canvas position. **Detach**,
**Attach To**, **Set Root** and **Remove Branch** modify the structure. Tree edits and layout moves
have separate undo/redo controls.

**Save** writes the canonical tree, including an incomplete draft. Reference validation reports
errors that prevent compilation. **Save Layout** writes positions separately; save tree changes
first. Both saves reject a file changed externally since it was opened. **Check Native** compiles
the current version 2 draft with discovered generated contracts and displays compiler diagnostics;
it does not execute the program or verify Player integration. The catalog discovers
registered project reference nodes and generated player-node contracts; discovery problems are
shown in the browser. Reference validation does not establish whether a project-generated native
node is executable. The Blackboard panel creates tree-scoped Bool, Int32, Int64, Float32, Float64,
Float2 and Float3 variables with optional defaults. IDs are stable and also serve as names. Generated
nodes expose compatible blackboard bindings in the selected node panel. Removing a variable leaves
its existing bindings visible for repair. Existing version 1 trees are not automatically migrated;
generated bindings require version 2. Agent/shared contracts and other value types remain read-only
in this editor. **Debug Selected** maps a matching live host onto this graph; **Preview** shares
the same view with the reference controls.

For a running agent, select its GameObject with a bootstrapped `ProductionTreeHost` and open
**AIBT > Attach Selected Host Trace**. The timeline reads the host's existing channel without
changing execution. Source-node highlighting requires a verified graph/program mapping and is not
inferred by this command; trace records remain available by runtime node index.

**AIBT > Behavior Tree Preview** runs the managed reference executor with built-in leaves and
discovered project reference providers. Managed/main-thread and nondeterministic nodes require
the corresponding explicit toggles. Generated native-only nodes need a reference behavior to
run here; a reference preview is not a live native-agent debugger. Each provider must create a
fresh behavior per node; its state is not migrated by the separate hot-reload preview.

To inspect complete production-node examples, import **Showcase Trees** from Unity Package Manager,
open **AIBT > Graph Editor**, and select an imported tree or use **Open Tree**. Each tree includes
a saved layout.

To run the production-boundary gameplay sample, import **Swarm Arena** from the same package's
**Samples** tab, then open its imported `SwarmArena.unity` scene. Its sample README describes the
controls, live telemetry, debugger attachment, and the boundary between live orientation and Player
benchmark evidence.

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
