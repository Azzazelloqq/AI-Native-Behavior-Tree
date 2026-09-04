# AI-native authoring and MCP

## Principle

MCP is a transport. AI usability comes from strict contracts, discoverability, safe operations, structured feedback, simulation, and documentation shared with the human editor.

The core remains model- and vendor-neutral.

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
- extract or inline subtrees;
- apply a domain patch transaction;
- request layout of the affected region.

### Verification

- validate and compile;
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

Approved protocol decision: `Planning~/Evidence/P7-031/implementation-proposal.md`.

The MCP server is an external `dotnet` process built on the official C# MCP SDK (stdio transport), launched by the AI client's own MCP configuration, never code loaded into Unity's Editor assembly graph. A thin, dependency-free Editor-side listener bridges it to a running Unity Editor instance over a discovery file. This requires the .NET SDK installed on the user's machine; no server binary is vendored inside the AIBT package. Full rationale and rejected alternatives: [ADR P6-001](decisions/ADR-P6-001-mcp-transport-and-permission-model.md).
