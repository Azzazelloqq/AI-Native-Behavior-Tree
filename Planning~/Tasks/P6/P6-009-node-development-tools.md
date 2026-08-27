# P6-009 — Node development tools

Status: `Draft`

## Objective

Expose `Documentation~/ai-and-mcp.md`'s "Node development" tool group over
MCP: generate a node from a maintained template, generate/update its tests
and manifest metadata, run analyzers/compile/test and show a reviewable
diff — implementing the safe-mutation "Generate -> Preview/Diff -> Analyze
-> Compile -> Test -> Explicit Apply" gate literally, on top of the
already-accepted `P2-004`/`P2-005` codegen pipeline.

## Depends on

- `P6-005` (MCP server host and permission enforcement).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Core MCP surface > Node development"
  and "Safe mutation protocol" sections (the exact gate this card must
  implement, not a shortened version of it).
- `Documentation~/burst-node-authoring.md` and the **Public Burst Nodes**
  package sample — the maintained authoring path (shard/catalog, generated
  `BurstAccess`) this card's "generate from template" must scaffold, not
  invent a competing one.
- `CodeGen~/AIBT.CodeGen` and `Documentation~/specifications/
  burst-node-abi-v1.md`/`burst-node-abi-v2.md` — the accepted analyzer/
  generator this card's "run analyzers, compile" step must invoke as-is.
- `Planning~/Evidence/P5-GATE/known-limitations.md` — this remains the
  first time in the project's evidence history a genuinely new custom node
  is generated and registered end-to-end; disclose that plainly rather than
  implying prior phases already exercised this path.

## Allowed changes

- The MCP assembly's node-development tool module (location per `P6-001`'s
  ADR).
- `Tests/Editor/Mcp/NodeDevelopment/` (new) or the equivalent location.
- `Planning~/Evidence/P6-009/`.

## Forbidden changes

- Any new codegen template mechanism competing with `CodeGen~/AIBT.CodeGen`
  — this card scaffolds callers of the existing generator, it does not fork
  it.
- Auto-applying generated code without the explicit-apply step; every stage
  before it must be inspectable and non-persisting.
- Registering a generated node into a production project's assembly
  without the caller's explicit apply confirmation.

## Deliverables

- A `generate-node` tool producing a reviewable shard/catalog scaffold
  (per the Public Burst Nodes sample shape) from a typed template input,
  written to a caller-visible location, not yet compiled into the project.
- A `preview-node-diff` tool showing the exact files/diff the generation
  would add, before any compile step runs.
- A `generate-node-tests-and-manifest` tool producing the paired test
  scaffold and manifest-registry entry for a generated node.
- An `analyze-and-compile-node` tool running the real Roslyn analyzer and a
  real Unity compile against the generated code, returning analyzer/compile
  diagnostics, never a bare pass/fail.
- A `test-node` tool running the generated node's own tests.
- An `apply-node` tool that is the only step that persists/registers the
  generated node into the project, requiring an explicit prior successful
  analyze/compile/test result to be re-affirmed, not silently assumed.

## Acceptance criteria

- The full gate (generate -> preview/diff -> analyze -> compile -> test ->
  explicit apply) is exercised end-to-end at least once against a real,
  non-trivial custom node (a Condition with typed blackboard read, per the
  sample), producing a node that actually executes through generated
  dispatch afterward.
- Calling `apply-node` without a prior successful analyze/compile/test
  result in the same session is rejected with a structured diagnostic.
- `preview-node-diff` output changes nothing on disk, verified by a
  before/after file-state comparison.
- A deliberately broken template input produces an analyzer/compile
  diagnostic, not a silent partial scaffold.

## Required verification

```text
real MCP client: full generate->preview->analyze->compile->test->apply gate
  against a real Condition node, ending in real generated-dispatch execution
apply-without-prior-verification refusal proof
preview-no-persistence proof
Verify-Static.ps1
clean Unity import and compile after apply
```

## Handoff notes

- `P6-011` (generated agent documentation) should use this card's actual
  gate as the source for its "how to add a custom node" recipe, not a
  paraphrase.
