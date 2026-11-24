# ADR P9-001: Managed agent layer

- Status: Accepted by owner plan approval, 2026-11-11
- Decision ID: AIBT-045

## Context

The native path is built for populations: generated Burst nodes, a catalog-pinned runtime asset,
an explicitly owned program, a host, a world bridge and typed facts and effects. That is the right
shape for a crowd and too much ceremony for one character. A reviewed consumer needed four
assemblies and several hundred lines to make one agent patrol and chase, and a node could not
touch a GameObject at all.

The package already contained a complete managed executor, `ReferenceExecutionMachine`, proven
equivalent to the native machine and used only for preview.

## Decision

Add a second, explicitly managed way to run a tree, in a new assembly `AIBT.Agents`
(references `AIBT.Runtime` and `AIBT.Authoring`; `AIBT.Runtime` gains no dependency).

1. `BehaviorTreeAgent` is a `MonoBehaviour` that compiles a tree once per asset, shares the
   compiled program between agents and drives one `ReferenceExecutionMachine` per agent on the
   main thread. Composite, decorator, timeout, cooldown and reactive semantics are exactly the
   existing machine's. No scheduler, world or catalog is involved.
2. A node is an ordinary class deriving from `BtAction` or `BtCondition` with a
   `[BtNode("project.name")]` attribute. Public fields are its parameters. A `BbKey<T>` field is
   a blackboard key the tree chooses. One instance exists per node per agent, so fields are
   per-agent state. Its manifest is derived by reflection and declares `MainThread`,
   non-deterministic execution; a managed node is never presented as Burst-compatible.
3. Gameplay code reads and writes the agent's Tree-scope blackboard by key name through
   `AgentBlackboard`. A write between ticks is stored at once and published to observers at the
   start of the next update. A write inside a leaf callback is recorded like a declared node
   write. A write is refused while an update is budget-suspended, mirroring ADR-P7-039.
4. A restart keeps blackboard values unless the caller asks for a reset. Root completion can
   restart automatically; the restart happens on the next tick, never within the same tick.

### Additive node-contract change

Parameter types `int32`, `string` and `blackboard-key` are added. They are valid only on
`Managed` or `MainThread` manifests and are never packed into the configuration blob: the managed
host delivers the authored value to the node. A `Managed`/`MainThread` manifest may also leave a
classic parameter unpacked. A `blackboard-key` parameter may state the blackboard type it
requires (`valueType`); validation requires the authored value to name a Tree-scope key of that
type. An optional parameter may document its default (`default`).

Every `Burst` manifest keeps the rule that each parameter has exactly one packing field, and its
canonical bytes are unchanged. Existing enum values, Burst ABI v2, generator output, the tree
formats and the compiled format are unchanged. Older tools reject the unknown types rather than
dropping them.

### Condition interrupts

Execution Semantics v1 makes a reactive composite abort and re-enter its running child on every
update. That is deterministic and suits stateless leaves, but a stateful action under a reactive
composite never progresses, and a memory composite never looks at a condition again once it has
chosen a child. Neither gives the ordinary "patrol until the enemy is visible" behavior with
resumable actions.

The managed layer therefore lets a condition node opt in, per node, through the `interrupt`
parameter every `BtCondition` carries (`none`, `self`, `lower-priority`, `both`; default `none`):

- `self`: after the condition succeeded, it is re-checked before each tick while its nearest
  ancestor composite is still active. When it stops holding, that composite abandons its running
  child and chooses again from its first child.
- `lower-priority`: after the condition failed, it is re-checked before each tick while its
  nearest ancestor selector is running a later child. When it starts holding, that selector
  abandons its running child and chooses again from its first child.

The owner must be a memory composite. Only the owner's running subtree is aborted; steps that
finished before it are not repeated. The machine gains one internal entry point for this,
`TryRequestReevaluation`, which reuses the existing abort traversal and cursor reset. It is not
reachable from a tree file and changes no existing node's behavior. An owner inside a parallel
branch is not on the single active path, so the layer falls back to restarting the tree.

Reactive composites keep their normative behavior. The managed layer documents that stateful
actions belong under a memory composite with an interrupting condition.

## Consequences

- The managed layer allocates and uses virtual dispatch and reflection at bootstrap. The
  zero-allocation-per-tick contract applies to the Burst path only and is not claimed here.
- A tree containing a managed node cannot be prepared for the native host, and a native-only
  generated node cannot run in a `BehaviorTreeAgent`. The editor validates a tree that uses a
  managed node with the managed policy.
- Condition interrupts are a managed-layer feature. The native path has no equivalent; adding a
  priority composite with the same meaning to both machines would need its own decision.
- The fixture-based reference preview cannot run a managed node, which needs its authored
  parameters and agent; it reports a clear fault.

## Amendment 2026-11-24 (P11-015)

The consequence "the editor validates a tree that uses a managed node with the managed policy" held
for the Graph window's tree list and not for MCP and the CLI. Without `<project>/.aibt/policy.json`,
MCP `validate` and `compile` used the built-in default (`ReferenceCompilationPolicy.Phase1`: no
managed, main-thread or non-deterministic nodes, no Agent or Shared scope), `prepare_runtime`
refused what `validate` refused, and `get_project_manifest` returned only an error. In a consumer
project without the file (P11-011, finding D1) the Simple Agent sample's trees were valid in the
tree list and invalid through MCP and the CLI, and a native tree with Agent-scope keys that the
graph bakes was refused by `prepare_runtime`.

Decision: the tree list's behaviour is the default. Without the file, `validate`, `compile` and
`prepare_runtime` (which is gated on `validate`) take their policy from one helper,
`ProjectNodeCatalog.DefaultPolicy`, which the tree list uses too: the managed layer's policy
(`ManagedTreeProgram.Policy`) for a tree with a managed node, decided on the expanded tree when it
references subtrees, otherwise the native default, which is the built-in default with Agent and
Shared scope allowed. `get_project_manifest` returns the trees and capabilities with
`"policy": null`. A file that exists and cannot be read (for example a missing required field) is
not applied and not silently ignored: the tools use the default and report warning AIBT9008 naming
the problem, in the `diagnostics` of `validate` and `compile` and in the manifest's
`policyWarnings`. With a readable file nothing changes: the file decides, for every tree.

Why: the file is how a project sets stricter or different rules, not a precondition for checking a
tree. A consumer who imports a sample and has no file must get the same answer from the graph and
from an agent. The managed policy is still the only one a managed tree is checked with, so a tree a
`BehaviorTreeAgent` refuses (an Agent-scope key, an observer block) is still refused.

Consequences: a `compile` without the file records the default policy in the program header, so its
`contentHash` differs from the earlier Phase 1 compile of the same tree.
`ReferenceCompilationPolicy.Phase1` is unchanged; `simulate` and the MCP authoring tools without the
file still use it for a native tree. `Schemas~/project-manifest.schema.json` makes `policy` nullable
and adds the optional `policyWarnings`; the error-only shape is no longer produced.
