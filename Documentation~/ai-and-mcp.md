# AI-native authoring and MCP

## Principle

MCP is a transport. AI usability comes from strict contracts, discoverability, safe operations, structured feedback, simulation, and documentation shared with the human editor.

The core remains model- and vendor-neutral.

Current command/native workflows and exact supported limits are in [CLI and MCP setup](cli.md).
Use `list_operations`/`get_operation_schema` rather than inspecting handler source. Native
preparation and live host tracing reuse the Editor baker and non-blocking trace reader.

For command-driven agents, start with [CLI and MCP setup](cli.md). CLI and MCP share the
Editor operation dispatcher. Explicit client requests can start/recover the bridge in an
already-open Editor; manual Start is optional for this path (AIBT-043). MCP resources and custom
tool discovery remain available. Neither adapter is a runtime dependency.

## Information available to an agent

- project manifest and supported capabilities;
- tree summaries and revisions;
- node catalog with searchable metadata;
- exact parameter and blackboard schemas;
- reads, writes, side effects, lifecycle, execution domain, determinism, and cost hints;
- project policy and style rules;
- examples, recipes, anti-patterns, and migrations;
- compiler diagnostics, tests, runtime traces, and benchmark results.

Agents should request summaries first and load detailed contracts only when needed. Tools support pagination and targeted lookup so large projects do not require placing every tree and node definition in context.

## Native-only nodes

The package's native node library (`aibt.native.*`, shard `aibt.native.standard`, 33 nodes in
`Native/Blackboard`, `Native/Flow`, `Native/Timing`, `Native/Signals` and `Native/Random`; see
[Native node library](native-node-library.md)) and a project's own Burst nodes are native-only.
What an AI client needs to know about them:

- They appear in `search_nodes` and `get_node_contract` like any node, with `executionDomain`
  `burst`. Their blackboard keys are not in the manifest's `reads`/`writes`: they are the node's
  `bindings`, listed by `get_node_contract` in `behaviorCaseContract.inputs` (member id and value
  type). A node is added with `parameters` and `bindings` in its node JSON (`create_tree`,
  `add_node`, an `add` in `apply_domain_patch`); the tree must be format version 2
  (`create_tree` with `formatVersion: 2`).
- They run on `NativeTreeAgent`, `ProductionTreeHost` and `TreeSimulation`, never in a
  `BehaviorTreeAgent`.
- `validate` checks them (types, bindings, policy) but executes nothing. `compile` is the reference
  compiler and fails with AIBT3012 on the first native node; `simulate` refuses them with AIBT2021;
  `run_tests` (behavior cases) fails with a `FactoryFault`. `prepare_runtime` compiles them for the
  native runtime against a catalog that declares them (a project `[AibtCatalogSet]` listing
  `StandardNativeShard` for the library), which is the check that the tree will run.
- `runtime_hosts` lists the host of a running `NativeTreeAgent` (under the agent's GameObject
  name) and `read_trace` reads it by that `instanceId`; a running agent can also be watched in the
  AIBT Graph in Play Mode. `runtime_simulations`, `describe_agent` and
  `read_agent_trace` work on a `TreeSimulation` the game registers with
  `TreeSimulationDebug.Register`; they were not tried with library nodes.

The [generated recipes](generated/recipes.md) take an agent through finding the nodes, building
a native tree (keys, a guard, a shared cooldown, signals, a random pick), validating and preparing
it, and preparing it again after a change. Each step was run against a live Editor through MCP and
the CLI.

## Node manifest

Every node type declares a stable ID and version, concise summary, intended and discouraged use, parameters, ports, allowed children, blackboard access, side effects, lifecycle statuses, threading domain, determinism, estimated cost category, examples, and deprecation or migration information.

The manifest is generated from the same metadata used by code generation, editor palettes, validation, and documentation. Duplicated hand-maintained catalogs are forbidden.

## Core MCP surface

### Discovery

- project and capability description;
- node search and contract lookup;
- tree and policy listing;
- schema and documentation resources.

### Authoring

- create tree;
- add, remove, move, replace, and configure nodes;
- declare and change blackboard keys;
- extract or inline subtrees (node payloads, `extract_subtree` / `inline_subtree`);
- subtree references (`ADR-P11-001`): `add_node` with a `subtree` object, `set_subtree_keys`,
  `extract_to_subtree`, `unpack_subtree`, `find_subtree_usages`, and `inspect_tree` with
  `expanded`; see [Subtrees](subtrees.md);
- apply a domain patch transaction;
- request layout of the affected region.

### Verification

- validate and compile (a tree with subtree references is checked and compiled as expanded, and a
  missing, duplicate or recursive subtree is a diagnostic);
- simulate a behavior case;
- run focused tests;
- inspect or explain diagnostics;
- inspect and compare runtime traces;
- run approved benchmark scenarios.

### Node development

- generate a node from a maintained template;
- generate or update node tests and manifest metadata;
- run analyzers, compile, test, and show a reviewable diff;
- register custom high-level tool providers.

## Safe mutation protocol

Mutating operations accept an expected revision and support dry-run. They return semantic and layout diffs, structured diagnostics, and the resulting revision. A transaction is atomic: invalid partial trees are not persisted.

Generated code follows this gate:

```text
Generate -> Preview/Diff -> Analyze -> Compile -> Test -> Explicit Apply
```

Permissions distinguish read, semantic edit, layout edit, code generation, compilation, test execution, benchmark execution, and arbitrary project integration. No tool receives broader access by default.

## Structured diagnostics

Diagnostics include a stable code, severity, tree and node identity, JSON path, message, related locations, and machine-applicable suggested operations when safe. Text-only compiler errors are not the public tool contract.

## Project policy

`.aibt/policy.json` expresses checkable conventions such as allowed execution domains, maximum depth, naming, required descriptions, forbidden nodes, warning levels, performance budgets, and preferred patterns. Prose guidance explains intent; the validator enforces what can be formalized.

The file is optional. Without it, `validate`, `compile` and `prepare_runtime` use the options the Graph window's tree list uses: a tree with a managed node is checked with the managed layer's policy (what a `BehaviorTreeAgent` accepts), any other tree with the native default, which allows Agent and Shared scope. `validate` then reports `"policyApplied": false`, and `get_project_manifest` returns the trees and capabilities with `"policy": null`. Add the file to set stricter or different rules for every tree in the project; a file that exists but cannot be read is not applied and is reported as warning AIBT9008 naming the problem (in `diagnostics`, and in the manifest's `policyWarnings`). The authoring tools without the file check a native tree without Agent or Shared scope. Fields and a minimal file: [Data formats](data-formats.md#project-policy-aibtpolicyjson); the decision: ADR-P9-001, amendment 2026-11-24.

## Custom MCP tools

Projects may register high-level tools, for example creating a combat subtree or validating an ability-system integration. A tool declares stable name, description, JSON input/output schemas, permissions, side effects, cancellation behavior, dry-run support, and owning assembly.

Do not expose every behavior node as a separate MCP tool. Generic semantic operations consume the node registry; custom tools represent meaningful project workflows.

## Agent documentation

The repository provides a short workflow guide, generated node catalog, recipes, anti-patterns, good and bad examples, and versioned migrations. Optional `AGENTS.md`, `SKILL.md`, or provider-specific adapters may be generated, but the canonical contracts remain schemas and authoring APIs.

## Domain patches

A domain patch is a semantic patch (`TreeDocument`) or a layout patch (`LayoutDocument`), never
both in one transaction -- matching the codebase's own type-level separation between semantic
and layout operations. On this MCP surface, both kinds are checked against a computed content
hash supplied by the caller (`expectedHash`/`contentHash` for semantic patches, an equivalent
hash for layout patches), not a revision counter: `TreeDocument.Revision` is never persisted to
`*.aibt.json`, so it always resets across the reload-per-call boundary every MCP call crosses
and cannot detect a real concurrent edit between two separate calls. (The in-process
`Editor/Editing`/`Editor/Patching` human-editor path -- a single live session, no reload --
still uses `TreeDocument.Revision` directly as its own precondition; only the MCP surface uses
the content-hash fix.) Dry-run is calling the transaction and not persisting the result; no
separate dry-run code path exists. A caller must always use the actual hash returned by the
last accepted patch as the expected value for its next one, never assume a fixed increment.
Full rationale: [ADR P6-002](decisions/ADR-P6-002-domain-patch-revision-and-diff-model.md).

## Transport and hosting

### Staged node compilation

`analyze_and_compile_node(start)` captures the staged `.cs` files (relative paths and contents)
and returns `attemptId`. An Editor main-thread hook imports them, waits for an existing compile,
then requests a fresh compilation. `check` requires that identity; a legacy `logPositionBefore`
alone receives AIBT9030 and must be replaced with a new start/check sequence.

The single attempt record lives in `Library/AIBT/node-compile-attempt.json`. Compilation events
verify both staging assemblies; rebuilt assemblies additionally require domain reload.
Unity's `assemblyCompilationNotRequired` event confirms an up-to-date assembly for the requested
compilation and needs no artificial reload. `Application.consoleLogPath` supplies
supporting diagnostics only. Pending checks survive domain reload. Changed staging, superseded
attempts and an Editor restart require a fresh start. A successful check returns its captured
content hash; test/apply retain their own hash and registry verification.

`apply_node` accepts a new Assets-relative directory only. It rejects escapes, rooted paths and
existing link/reparse ancestry before moving files. This is the single-client write boundary;
it does not promise protection against concurrent filesystem replacement by another process.

Successful apply removes the staging-only companion catalog source after moving the node;
the verification catalog is never shipped with the destination. Native test dispatch owns
background-thread-compatible storage and disposes it after the request.

Approved protocol decision: `Planning~/Evidence/P7-031/implementation-proposal.md`.

The MCP server is an external `dotnet` process built on the official C# MCP SDK (stdio transport), launched by the AI client's own MCP configuration, never code loaded into Unity's Editor assembly graph. A thin, dependency-free Editor-side listener bridges it to a running Unity Editor instance over a discovery file. This requires the .NET SDK installed on the user's machine; no server binary is vendored inside the AIBT package. Full rationale and rejected alternatives: [ADR P6-001](decisions/ADR-P6-001-mcp-transport-and-permission-model.md).
