# ADR P6-022: Generic native-dispatch test-harness translator

- Status: Accepted 2026-08-31 (spike addendum 2026-08-31)
- Date: 2026-08-31
- Decision ID: AIBT-033

## Context

`P6-009`'s own `test-node` tool was found, before implementation, to have no cheap way to satisfy
its literal acceptance criterion ("producing a node that actually executes through generated
dispatch"). The only existing example of driving generated dispatch,
`Tools~/Verification/P2/CodeGen/SampleGolden/PublicBurstNodeSampleGoldenTests.cs.txt` (687 lines),
hand-computes every field offset, ordinal, binding-table entry, and transaction-control value for
the one specific sample node it tests. This card decides, on paper, whether and how to build a
generic translator from a compiled shard's own `AibtGeneratedMetadata.CanonicalDescriptorJson` into
the `NativeBurstDispatchWorkspaceShapeV2`/`NativeBurstDispatchWorkspaceOwnerV2` structures real
dispatch execution requires.

## Research findings (this session, against the real source)

1. **The case/field mapping is fully mechanical and verified correct against the real constructors,
   for the non-`Registered`, non-`FixedBytes`, non-async subset.** Reading
   `Authoring/Registry/Generated/GeneratedShardMetadataMaterializer.cs`'s own `MaterializeArtifact`
   confirms it already parses `CanonicalDescriptorJson` into `GeneratedNodeDescriptor` objects,
   each carrying `Configuration`/`Memory`/`Bindings` as lists of `GeneratedStorageField`/
   `GeneratedBindingDescriptor` with real `Offset`/`Size`/`Alignment`/`Encoding` per field --
   exactly the per-field data the translator needs, already accepted, not duplicated. Reading
   `Runtime/Execution/Burst/Dispatch/NativeBurstDispatchContractsV2.cs`'s real
   `NativeBurstDispatchCaseV2` constructor (`typeNumericId, typeVersion, catalogCaseIndex,
   firstConfigurationField, configurationFieldCount, configurationSize, firstMemoryField,
   memoryFieldCount, memorySize, phases, possibleStatuses, hasRandomStream, firstBinding,
   bindingCount`) confirms every one of its fields maps directly from a `GeneratedNodeDescriptor`
   plus a running append-offset the translator maintains while flattening one or more descriptors
   into shared `NativeBurstDispatchFieldV2`/`NativeBurstDispatchBindingV2` arrays -- no field
   requires information the descriptor JSON lacks.
2. **Real, decisive finding: the two "field encoding" vocabularies are NOT numerically aligned --
   a naive cast would silently corrupt dispatch behavior.** `Authoring/Registry/Generated/
   GeneratedNodeContracts.cs`'s `GeneratedFieldEncoding` (13 values: `Bool8=0` ... `FixedBytes=11,
   GeneratedHandle=12, Registered=13`) and `Runtime/Execution/Burst/Dispatch/
   NativeBurstDispatchContractsV2.cs`'s `NativeBurstDispatchFieldEncodingV2` (12 values: `Boolean=0`
   ... `GeneratedHandle=11`, no `FixedBytes` or `Registered` value at all) disagree on `GeneratedHandle`'s
   own numeric value (12 vs. 11) and have no dispatch-side equivalent for `FixedBytes`/`Registered`
   whatsoever. Confirmed by direct enum comparison, not assumed. A translator that cast the byte
   value across these two enums instead of mapping by name would silently misclassify fields --
   exactly the kind of defect that would only surface as corrupted node behavior at runtime, not a
   compile or validation error.
3. **`Registered`-encoded fields need further, not-yet-resolved design work before the translator can
   claim to handle them.** How the dispatch ABI represents a `Registered`-encoded configuration/
   memory field at the *field* level (distinct from how a *binding*'s own type role already
   references a registered type by raw numeric ID, confirmed in the golden test's own `_bindings[5]`
   using a bare `9038502846612247724UL` typeNumericId) was not fully traced in this session's own
   research. Forcing a mapping here without that research would risk exactly what this card's own
   Deliverables section explicitly warns against: "forcing a design that silently drops fidelity."
4. **The async-operation binding shape (`AsyncOperation`, a 2-type-pair binding covering start and
   cancel payloads) and completion/command/transaction-ledger request-side construction are a
   materially different, larger problem than case/field/binding *shape* translation.** Reading the
   golden test's own `AsyncRequestBuffers` shows the *request*-side buffers (resolved-binding arena
   addresses, transaction ledger tokens, completion/command arrays) are hand-authored per test call,
   not derived from any compiled metadata even in the existing golden test -- they are inherently
   caller/harness-owned runtime state, not something "translating the descriptor" alone produces for
   any caller, generic or otherwise. This is a real, useful clarification of the card's own framing:
   the achievable "generic from metadata" translation is the workspace *shape* (cases/fields/
   bindings/handshake); the workspace *request* (per-call buffers) was never metadata-derived even in
   the one existing example, and remains a harness-authored concern for whichever future tool builds
   on this translator.

## Decision

1. **The translator reads `CanonicalDescriptorJson` (via the already-accepted, unmodified
   `GeneratedShardMetadataMaterializer.MaterializeArtifact`) for per-node case/field/binding shape
   data, plus the generated catalog type's own reflected fingerprint properties (`Fingerprint`,
   `NodeRegistryFingerprint`, `ConfigurationLayoutFingerprint`, `MemoryLayoutFingerprint`,
   `AccessLayoutFingerprint`) for the `BurstCatalogHandshake`, mirroring the golden test's own
   `GeneratedHandshake()` reflection technique exactly.** No new metadata source is needed for the
   case/field/binding shape; this answers the card's own first Deliverable question directly.
2. **Field-encoding translation is by explicit name-to-name mapping, never a numeric cast.** The
   translator must switch on `GeneratedFieldEncoding`'s named values and emit the corresponding
   named `NativeBurstDispatchFieldEncodingV2` value -- confirmed necessary by finding 2 above, a
   concrete, load-bearing requirement any implementation must respect.
3. **Scope of proven coverage: built-in-typed configuration/memory fields and non-async binding
   kinds (`BlackboardRead`/`BlackboardWrite`/`BlackboardReadWrite`/`SnapshotRead`/`EffectCommand`)
   for a single-case (non-combined) workspace shape.** This covers Condition/Action nodes shaped
   like the sample's own `ThresholdCondition` -- the most common node shape. `Registered`-encoded
   fields and the `AsyncOperation`/`Completion` binding pair are explicitly NOT covered by this
   decision's own verified mapping; a future card must research and design those separately before
   claiming them.
4. **Workspace *shape* translation only -- workspace *request* (per-call buffer) construction stays
   a caller-owned harness concern, as it already is in the one existing example.** `P6-009`'s
   `test-node` tool (or any future MCP verification tool built on this translator) must still author
   its own request buffers for whatever specific tick sequence it wants to exercise; this ADR does
   not claim otherwise.

## Spike addendum (2026-08-31, follow-up session) -- the disposable spike is now complete

The isolated-project spike this ADR originally deferred was run to completion.
`Spikes~/GenericNativeDispatchTestHarness/` implements `GenericNativeDispatchTranslatorV1` exactly
per the case/field mapping and name-based encoding translation decided above, and
`GenericNativeDispatchSpikeTests.ThresholdCondition_GenericallyTranslatedDispatch_ReadsTypedBlackboardValue`
drives a real, unmodified `ExecuteImmediate` through a workspace shape built entirely from
`GeneratedShardMetadataMaterializer`-parsed compiled metadata (no hand-copied per-node offsets),
via `Run-GenericNativeDispatchTestHarnessSpike.ps1` (a scoped-down copy of
`Tools~/Verification/P2/CodeGen/Build-And-Verify.ps1`'s own "SampleUnityProject" stage). Result:
**1/1 pass** -- Enter succeeds, Tick below the configured threshold reads `Failure`, Tick at/above
it reads `Success`, matching the real `PublicBurstNodeSampleGoldenTests` scenario's own
independently hardcoded result for the identical semantics.

**Real finding beyond the two already recorded above: a workspace's `Cases` array must be
positionally self-consistent, starting at index 0.** `NativeBurstDispatchWorkspaceOwnerV2.
TryCreate`'s own `ValidateShape` rejects any case whose `CatalogCaseIndex` does not equal its own
array position (`Cases[i].CatalogCaseIndex == i`), and `BurstDispatchBridgeCoreV2.
TryGetExecutionRequest` forwards `NativeBurstDispatchRequestV2.CatalogCaseIndex` verbatim into the
generated `ExecuteImmediate`'s own `switch (catalogCaseIndex)` -- the same value serves both the
workspace's internal bookkeeping and the real per-node dispatch selector. The practical
consequence: **the real, two-node `Samples~/BurstNodes` sample cannot be used to spike an isolated
single-case shape for `ThresholdConditionNode` specifically**, because that node sits at real
dispatch index 1 (after `AsyncWriteActionNode` at index 0, per `GeneratedMetadataEmitter`'s own
alphabetical `TypeId` ordering) -- a single-case shape targeting index 1 alone fails `ValidateShape`
before it ever reaches dispatch, and reaching index 1 legitimately requires the workspace to also
carry a structurally valid index-0 case, which for this sample means translating
`AsyncWriteActionNode`'s own `AsyncOperation`/`Completion` bindings -- explicitly out of this card's
decided scope (finding 4 above). This was confirmed empirically (a first attempt using
`ThresholdConditionNode` directly failed `TryCreate` with `InvalidEncoding` for exactly this
reason), not assumed. The spike was completed instead against a dedicated, disposable single-node
shard (`Spikes~/GenericNativeDispatchTestHarness/Harness/Node/`, a field-for-field copy of
`ThresholdConditionNode`'s own shape, decorated with the real `[AibtBurstNode]`/`[AibtCatalogShard]`
attributes and compiled by the same checked-in Roslyn analyzer), which the generator necessarily
assigns dispatch index 0 as its only node -- a legitimate way to prove the translator against real
generated dispatch without smuggling the excluded async case in, but it means this spike does not,
by itself, prove the translator against a pre-existing *multi-node* catalog. A future card widening
coverage to a specific node inside a larger real catalog (e.g. one added live via `P6-009`'s
`generate-node`/`apply-node`) must translate that catalog's full case prefix (`0..targetIndex`), not
an isolated single case picked out of the middle.

The two enum-mapping findings recorded above are now also empirically confirmed, not just verified
by inspection: the translator's name-to-name `GeneratedFieldEncoding` -> `NativeBurstDispatchFieldEncodingV2`
switch and `GeneratedBindingKind` -> `NativeBurstDispatchBindingKindV2` switch both round-tripped
correctly through real dispatch for the proven scope (`GeneratedHandle`, `UInt32`, `BlackboardRead`).
A second real structural requirement was found and is now load-bearing in the translator: for every
case, `NativeBurstDispatchCanonicalInputV2.CaseRanges` must carry exactly two entries per case
(configuration, memory) and `BindingRanges` exactly two per binding (primary, secondary) -- not one
of each, as a first draft of the translator assumed (also caught the same way, via a real
`TryCreate` `InvalidEncoding` rejection, not by inspection alone).

`Registered`-encoded fields and the `AsyncOperation`/`Completion` binding pair remain explicitly
unproven and undesigned at the field-encoding level, per finding 3/4 above -- unchanged by this
addendum. Verified: `Verify-Static.ps1` passed after this addendum (only documentation/planning and
disposable `Spikes~/` files changed; no production file was touched, per this card's own
Forbidden-changes clause). See `Planning~/Evidence/P6-022/README.md` for the full run record.

## Consequences

- A future implementation card builds the translator into production per this ADR's own case/field
  mapping and encoding name-mapping requirement (both now proven, not just verified by inspection),
  and widens `P6-009`'s `test-node` to actually drive generated dispatch for built-in-typed,
  non-async, single-case-reachable node shapes -- explicitly accounting for the dispatch-index
  contiguity finding above when the target node is not already at index 0 in its real catalog.
- `Registered`-encoded fields and the `AsyncOperation`/`Completion` binding pair remain explicitly
  unproven and undesigned at the field-encoding level; a future card must research and resolve that
  gap separately before claiming coverage.
- No production file (`Runtime/Execution/Burst/Dispatch/`, `Authoring/Registry/Generated/`,
  `CodeGen~/AIBT.CodeGen`) was touched, per this card's own Forbidden-changes clause -- the spike and
  its dedicated single-node fixture live entirely under `Spikes~/GenericNativeDispatchTestHarness/`.
