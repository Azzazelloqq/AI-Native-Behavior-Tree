# Specification conventions

These specifications are normative for AIBT `0.x` development until superseded by an accepted architectural decision.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are requirements. An implementation agent may not reinterpret them. If a task conflicts with a normative specification, the agent stops and reports the conflict before changing code.

## Versioning

- Each public semantic contract has an explicit version.
- Persisted documents record their format version.
- Compiled programs record semantic, compiler, and node-registry versions.
- Breaking persisted-format changes require deterministic migrations and fixtures.
- Breaking runtime or node API changes require a decision record and migration notes.

## Observable behavior

Observable behavior consists of root status, node lifecycle events, blackboard changes, emitted commands, aborts, diagnostics, and documented latency. Internal instruction order is not observable unless a specification makes it so.

## Terminology

- **Update**: one request to progress a tree instance.
- **Execution pass**: processing performed for one scheduler phase.
- **Node step**: one complete lifecycle callback or runtime instruction; it is never split by a budget.
- **Tree instance**: per-agent mutable state bound to one immutable compiled program.
- **Activation**: interval from a node's `Enter` through its terminal or aborted `Exit`.
- **Active child**: child currently holding `Running` state for a composite.
- **Semantic order**: ordered child list in the canonical tree document.
- **Budget yield**: internal scheduler suspension, never a public node status.
