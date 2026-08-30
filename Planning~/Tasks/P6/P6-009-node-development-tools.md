# P6-009 — Node development tools

Status: `Draft`

## Scope correction (2026-08-30)

`test-node`'s own literal wording ("running the generated node's own tests") and this card's
acceptance criteria ("producing a node that actually executes through generated dispatch
afterward") were found, before implementation, to assume a capability that does not exist: a
generic way to drive an arbitrary generated node through real Burst dispatch
(`NativeBurstDispatchWorkspaceOwnerV2`/`ExecuteImmediate`). The only existing example,
`Tools~/Verification/P2/CodeGen/SampleGolden/PublicBurstNodeSampleGoldenTests.cs.txt`, hand-computes
every field offset/ordinal/binding-table entry for one specific, already-known sample node --
there is no reusable translator from a compiled node's own descriptor metadata into the native
dispatch-workspace shape real execution requires, and `burst-node-abi-v2.md`'s opaque-context rule
rules out any reflection shortcut. Building that translator generically is a substantial new
capability in its own right, spun off into its own decision card, `P6-022`, rather than built ad
hoc mid-card (owner-confirmed via `AskUserQuestion`, mirroring `P6-008`'s `P6-015` split).

`test-node` is narrowed to what is genuinely, honestly provable without that translator: the
compiled shard's `AibtGeneratedMetadata` is structurally valid and registry-materializable
(`GeneratedShardMetadataMaterializer.MaterializeArtifact` + `GeneratedNodeRegistry.Build`, both
real, already-accepted production entry points) -- real verification, just not runtime dispatch
execution. This card's own acceptance criteria below are corrected to match; `P6-022`, once
accepted and implemented, is expected to widen `test-node` to genuine dispatch execution as a
follow-up.

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
- A `test-node` tool proving the generated node's compiled metadata is structurally valid and
  registry-materializable (per this card's Scope correction, above) -- not, for this card, genuine
  dispatch execution, deferred to `P6-022`.
- An `apply-node` tool that is the only step that persists/registers the
  generated node into the project, requiring an explicit prior successful
  analyze/compile/test result to be re-affirmed, not silently assumed.

## Acceptance criteria

- The full gate (generate -> preview/diff -> analyze -> compile -> test ->
  explicit apply) is exercised end-to-end at least once against a real,
  non-trivial custom node (a Condition with typed blackboard read, per the
  sample), producing a node that is real, compiled, and found by the real
  project's node registry/`NodeCatalogQuery` afterward -- not, for this
  card, a proof that it executes through generated dispatch (see Scope
  correction; that proof is `P6-022`'s own deliverable).
- Calling `apply-node` without the staged content currently matching a
  prior successful analyze-and-compile-node/test-node check (re-verified by
  content hash, not a trusted caller claim or session state) is rejected
  with a structured diagnostic.
- `preview-node-diff` output changes nothing on disk, verified by a
  before/after file-state comparison.
- A deliberately broken template input produces an analyzer/compile
  diagnostic, not a silent partial scaffold.

## Required verification

```text
real MCP client: full generate->preview->analyze->compile->test->apply gate
  against a real Condition node and a real Action node, ending with the
  applied node real, compiled, and registry-searchable
apply-without-a-matching-clean-check refusal proof
preview-no-persistence proof
Verify-Static.ps1
clean Unity import and compile after apply
```

## Handoff notes

- `P6-011` (generated agent documentation) should use this card's actual
  gate as the source for its "how to add a custom node" recipe, not a
  paraphrase.
