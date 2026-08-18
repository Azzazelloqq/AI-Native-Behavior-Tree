# Agent development rules

These rules apply to the entire repository.

## Working agreement

1. Do not modify code or project files without an explicit user request.
2. For a new problem or feature, discuss direction and constraints first, then produce a detailed plan, and implement only after approval.
3. Preserve the architecture described in `Documentation~/architecture.md` and the decisions in `Documentation~/decisions.md`.
4. Prefer DRY, KISS, SOLID, clean code, and established Unity/C# practices. Do not add speculative infrastructure.
5. Test expected behavior. Never reshape tests to preserve an incorrect implementation.
6. Keep changes scoped. Do not silently add adjacent features.
7. Before finishing, verify that every requested item is complete and that no unrequested behavior was introduced.
8. Read `Planning~/AGENT_WORKFLOW.md` and the assigned work-item card before implementation. Work only on a task whose dependencies are complete.
9. Normative specifications in `Documentation~/specifications/` cannot be changed as an incidental part of an implementation task.
10. Canonical code, API names, diagnostics, schemas, and documentation are written in English. Translations are supplemental and cannot define behavior.

## Architectural boundaries

- `Runtime` must not depend on `UnityEditor`, MCP, an LLM provider, visual authoring, or DOTS Entities.
- DOTS/ECS is not a required dependency. The runtime is built on Burst, Jobs, and Unity Collections.
- The performance contract is zero GC allocations per tick after initialization for the Burst execution path.
- Unity API and managed integrations execute through explicit main-thread adapters and command buffers.
- Authoring data, presentation layout, and compiled runtime data are separate models.
- Canonical semantic files use `.aibt.json`; editor layout uses `.aibt.layout.json`.
- The visual editor must never make semantic behavior depend on node coordinates, colors, groups, or comments.
- MCP is a transport and integration layer, not a runtime dependency or source of truth.
- Public node contracts and data formats are versioned. Breaking changes require a migration and a documented decision.

## Runtime and node rules

- Burst-compatible nodes use unmanaged data and declare blackboard reads, writes, side effects, execution domain, determinism, and cost category.
- Managed nodes are an explicit fallback and must never be presented as Burst-compatible.
- Avoid virtual dispatch, reflection, hidden global state, per-tick allocations, and direct `UnityEngine.Object` access inside jobs.
- A node has one responsibility and defined `Running`, cancellation, success, and failure semantics.
- Scheduling changes may affect timing but must not silently change tree semantics.
- Unity Web uses the documented single-thread backend; do not compile out core behavior merely because worker jobs are unavailable.

## Quality gates

- Add behavior-focused tests for every semantic change.
- Add or update benchmarks for performance-sensitive changes.
- Run validation, tests, and relevant benchmarks before declaring work complete.
- Update documentation, schemas, examples, and `CHANGELOG.md` when their contracts change.
- Do not commit generated IDE files, benchmark output, local MCP caches, secrets, or credentials.
- Submit the handoff report required by `Planning~/AGENT_WORKFLOW.md`; do not mark a work item complete when a required verification command was unavailable.
