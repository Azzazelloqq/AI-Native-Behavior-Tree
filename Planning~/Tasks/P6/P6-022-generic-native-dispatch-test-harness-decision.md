# P6-022 — Generic native-dispatch test-harness decision

Status: `Draft`

## Objective

Decide whether and how to build a generic translator from a compiled Burst-node shard's own
`AibtGeneratedMetadata.CanonicalDescriptorJson` into the low-level `NativeBurstDispatchWorkspaceShapeV2`/
`NativeBurstDispatchWorkspaceOwnerV2` structures real dispatch execution requires — so an MCP tool
(or any other automated caller) can genuinely tick an arbitrary generated node through its real
generated dispatch, not just prove it compiled and its metadata is registry-valid.

This card exists because `P6-009`'s own `test-node` tool was found, before implementation, to have
no cheap way to satisfy its card's literal acceptance criterion ("producing a node that actually
executes through generated dispatch"). The only existing example of driving generated dispatch,
`Tools~/Verification/P2/CodeGen/SampleGolden/PublicBurstNodeSampleGoldenTests.cs.txt` (688 lines),
hand-computes every field offset, ordinal, binding table entry, and transaction-control value for
the one specific sample node it tests -- there is no generic, reusable "run this arbitrary
generated node with these inputs" API anywhere in the project. `burst-node-abi-v2.md`'s own
opaque-context change rules out a reflection shortcut too: `BurstTickContext` and its siblings are
native-backed, Runtime-private carriers with no public constructor a caller outside `AIBT.Runtime`
could legitimately build by hand.

Per explicit owner decision (`AskUserQuestion`, this session): `P6-009`'s own `test-node` is
narrowed to compile-clean + registry-materialization-valid only (real, meaningful checks, just not
runtime execution) rather than building this translator ad hoc mid-card. This card decides the
translator's design on paper first.

## Depends on

- `P6-009` (the card whose `test-node` tool this unblocks, once accepted).
- `P2-004`/`P2-005` (the accepted codegen pipeline `CanonicalDescriptorJson`'s own shape comes
  from).

## Required reading

- `Tools~/Verification/P2/CodeGen/SampleGolden/PublicBurstNodeSampleGoldenTests.cs.txt` -- the one
  existing example of the target shape (`NativeBurstDispatchWorkspaceShapeV2`,
  `NativeBurstDispatchCaseV2`, `NativeBurstDispatchFieldV2`, `NativeBurstDispatchBindingV2`,
  `NativeBurstDispatchWorkspaceOwnerV2.TryCreate`/`TryBeginRequest`/`TryAcquireImmediateBatch`/
  `TryConsumeResult`), hand-built for one known node -- this card's own translator must produce the
  equivalent shape generically, from real compiled data, not by copying this file's hardcoded
  numbers.
- `Authoring/Registry/Generated/GeneratedShardMetadataMaterializer.cs` -- the existing, accepted
  descriptor-JSON reader (`configuration`/`memory`/`bindings` field offset/size/alignment/encoding
  data) this card's translator would consume as its own input, not duplicate.
- `CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs` -- confirms exactly what the descriptor JSON
  does and does not carry, so this card knows precisely what the translator can derive versus what
  it would need from elsewhere (e.g. `BurstCatalogHandshake`'s own fingerprint values, read via
  reflection off the generated catalog the same way the golden test's own `GeneratedHandshake()`
  does).
- `Documentation~/specifications/burst-node-abi-v2.md` -- the opaque-context rules any translator
  design must respect (no direct construction of `BurstTickContext` etc.; only the real generated
  `ExecuteImmediate`/dispatch entry points may ever touch them).

## Allowed changes

- `Spikes~/GenericNativeDispatchTestHarness/` (new, disposable) -- proves the recommended
  translator design against a real compiled shard (the sample, or a fresh node from `P6-009`'s own
  templates), driving real `ExecuteImmediate` through a generically-constructed workspace shape,
  mirroring `P6-002`'s own spike-before-ADR methodology.
- `Planning~/Evidence/P6-022/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Runtime/Execution/Burst/Dispatch/`, `Authoring/Registry/Generated/`,
  or `CodeGen~/AIBT.CodeGen` -- this card decides on paper; a separate future card implements it
  and widens `P6-009`'s `test-node` to actually use it.
- Constructing any opaque, native-backed context type (`BurstTickContext` and siblings) by any
  means other than the real generated dispatch entry point calling into them -- the translator's
  job is building the workspace/request *inputs* those entry points consume, never bypassing them.
- Assuming the sample's own two nodes (Condition/Action) exhaust the design space -- the
  translator must be argued correct for the general field-encoding/binding-kind vocabulary
  `burst-node-abi-v1.md` defines, not just the two shapes already seen.

## Deliverables

- A decision on the translator's design: does it read `CanonicalDescriptorJson` directly (per-field
  offset/encoding data already present) plus the generated catalog's own reflected
  fingerprint/hash properties (mirroring `GeneratedHandshake()`), or does it need something the
  descriptor JSON doesn't currently carry (disclose plainly if so, rather than forcing a design
  that silently drops fidelity)?
- A disposable spike proving the recommended translator against a real compiled shard: constructs
  a `NativeBurstDispatchWorkspaceShapeV2` and `NativeBurstDispatchWorkspaceOwnerV2` purely from
  reflected/parsed compiled data (no hand-copied per-node offsets), successfully drives
  `ExecuteImmediate` through at least one full Enter/Tick/Exit cycle, and produces the same
  observable result the existing hardcoded golden test already proves for the sample node.
- A proposed ADR recording the decision, its rationale, and exactly what node shapes/binding kinds
  remain unproven (if any).

## Acceptance criteria

- The spike drives a real, unmodified `ExecuteImmediate` call through a workspace shape built
  entirely from compiled metadata -- no per-node hardcoded field offsets copied from the golden
  test.
- The spike's own result (status transitions, blackboard/command effects) matches what the
  existing golden test already independently proves for the same sample node, confirmed by direct
  comparison, not assumed.
- The ADR states plainly which binding kinds/field encodings the translator handles and which (if
  any) remain unproven, rather than implying full generality without evidence.

## Required verification

```text
Verify-Static.ps1
disposable spike: real compiled shard, live Unity MCP execute_code, ExecuteImmediate driven
  through a generically-constructed workspace shape
cross-check against the existing golden test's own independently-hardcoded result for the same
  sample node
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as a narrowing of
  `P6-009`'s own scope mid-session, mirroring `P6-008`'s `P6-015` split.
- If accepted, a future implementation card applies the ADR to production and widens `P6-009`'s
  `test-node` (and, if useful, a to-be-decided MCP verification tool) to actually drive generated
  dispatch instead of stopping at compile-clean + registry-valid.
