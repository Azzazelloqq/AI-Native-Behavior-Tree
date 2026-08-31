# P6-022 Generic native-dispatch test-harness decision evidence

## Result

**Done.** `ADR-P6-022` (`AIBT-033`) decides the translator's design (read `CanonicalDescriptorJson`
via the already-accepted `GeneratedShardMetadataMaterializer` for case/field/binding shape, plus the
generated catalog's own reflected fingerprints for the handshake, mirroring the existing golden
test's own `GeneratedHandshake()` technique) and the mapping is now verified both by direct reading
of the real `NativeBurstDispatchCaseV2`/`NativeBurstDispatchFieldV2` constructors/enums **and** by a
completed live spike driving real generated dispatch. The card's own required live spike (not
completed in the original session) has since been run to completion — see the addendum below.

## Real finding: the two field-encoding enums are not numerically aligned

`GeneratedFieldEncoding` (`Authoring/Registry/Generated/GeneratedNodeContracts.cs`, 13 values
including `FixedBytes=11`, `GeneratedHandle=12`, `Registered=13`) and
`NativeBurstDispatchFieldEncodingV2` (`Runtime/Execution/Burst/Dispatch/NativeBurstDispatchContractsV2.cs`,
12 values, `GeneratedHandle=11`, no `FixedBytes`/`Registered` equivalent) disagree on
`GeneratedHandle`'s own numeric value and have no dispatch-side value at all for `FixedBytes`/
`Registered`. A translator that cast the byte value across these two enums instead of mapping by
name would silently misclassify fields -- a defect that would only surface as corrupted node
behavior at runtime, not a compile or validation error. Confirmed by direct enum comparison, and now
also empirically: the translator's real name-to-name switch round-tripped `GeneratedHandle`/`UInt32`
correctly through a real `ExecuteImmediate` call.

## Spike addendum (2026-08-31, follow-up session): completed

`Spikes~/GenericNativeDispatchTestHarness/` implements `GenericNativeDispatchTranslatorV1` and
proves it via `GenericNativeDispatchSpikeTests.
ThresholdCondition_GenericallyTranslatedDispatch_ReadsTypedBlackboardValue`, run through
`Run-GenericNativeDispatchTestHarnessSpike.ps1` (a scoped-down copy of
`Tools~/Verification/P2/CodeGen/Build-And-Verify.ps1`'s own "SampleUnityProject" stage: an isolated
Unity project referencing `com.azzazello.aibt` as a local `file:` package, real Roslyn-generated
dispatch, real `NativeBurstDispatchWorkspaceOwnerV2`/`ExecuteImmediate`).

**Final result: 1/1 pass.** Enter succeeds; Tick below the configured threshold reads `Failure`;
Tick at/above it reads `Success` -- matching the real `PublicBurstNodeSampleGoldenTests.
ThresholdCondition_GeneratedDispatchReadsTypedBlackboardValue`'s own independently hardcoded result
for the identical semantics.

Three real defects were found and fixed by the spike's own iterate-fix-rerun cycle, each confirmed
by a genuine Unity batchmode failure, not by inspection alone:

1. A `using`-declared `readonly struct` local cannot feed an `in` parameter through a property
   access directly (`CS8156`) -- fixed by binding the property to a local first.
2. `NativeBurstDispatchCanonicalInputV2.CaseRanges`/`BindingRanges` require exactly two entries per
   case (configuration, memory) and per binding (primary, secondary) respectively, not one --
   `NativeBurstDispatchBindingValidationV2.ValidateShapeMetadata` rejects a mismatched count with
   `InvalidEncoding`.
3. **The load-bearing finding**: `NativeBurstDispatchWorkspaceOwnerV2.TryCreate`'s `ValidateShape`
   requires a workspace's `Cases` array to be positionally self-consistent starting at index 0
   (`Cases[i].CatalogCaseIndex == i`), and the same `CatalogCaseIndex` value is forwarded verbatim by
   `BurstDispatchBridgeCoreV2.TryGetExecutionRequest` into the real generated `ExecuteImmediate`'s
   own `switch (catalogCaseIndex)`. Practically: the real, two-node `Samples~/BurstNodes` sample's
   `ThresholdConditionNode` sits at real dispatch index 1 (after `AsyncWriteActionNode` at index 0);
   an isolated single-case shape targeting index 1 alone is rejected outright, and reaching index 1
   legitimately requires also including a structurally valid index-0 case -- for this sample, that
   means translating `AsyncWriteActionNode`'s own `AsyncOperation`/`Completion` bindings, explicitly
   out of this card's decided scope. Resolved by giving the spike its own dedicated, disposable
   single-node shard (`Spikes~/GenericNativeDispatchTestHarness/Harness/Node/`, a field-for-field
   copy of `ThresholdConditionNode`'s own shape) rather than the real sample, so the generator
   necessarily assigns it dispatch index 0. This is a genuine scope caveat, not a workaround: a
   future card widening `test-node` to a specific node inside a real, larger, already-existing
   catalog must translate that catalog's full `0..targetIndex` case prefix, not an isolated case.

## What is and is not verified

```text
Verified by direct code reading AND a completed live spike:
- GeneratedShardMetadataMaterializer.MaterializeArtifact already parses CanonicalDescriptorJson
  into per-field Offset/Size/Alignment/Encoding data -- confirmed sufficient input, and used as-is
  by the spike's translator.
- NativeBurstDispatchCaseV2's real constructor -- every parameter maps mechanically from a
  GeneratedNodeDescriptor plus a running append-offset; proven by a real ExecuteImmediate call.
- GeneratedFieldEncoding vs. NativeBurstDispatchFieldEncodingV2 -- NOT numerically aligned; the
  translator's name-based mapping was exercised end-to-end for GeneratedHandle/UInt32.
- GeneratedBindingKind vs. NativeBurstDispatchBindingKindV2 -- share the same ordinal layout, still
  mapped by name (not a raw cast); exercised end-to-end for BlackboardRead.
- Workspace *request* (per-call buffer) construction was never metadata-derived even in the
  existing golden test -- confirmed again by the spike's own hand-authored request buffers.
- NEW: a workspace's Cases array must be positionally self-consistent from index 0 -- the real
  reason a single node cannot be isolated from the middle of an existing multi-node catalog.
- NEW: canonical case/binding ranges require exactly 2 entries per case/binding, not 1.

Still NOT verified (unchanged from the original ADR, explicitly out of this card's decided scope):
- Registered-encoded field handling.
- The AsyncOperation/Completion binding pair's own shape.
- Translating a specific non-zero-index node out of an existing multi-node catalog (requires
  translating the full 0..targetIndex prefix, not attempted here).
```

Verify-Static.ps1 -- passed after this addendum (only documentation/planning and disposable
`Spikes~/GenericNativeDispatchTestHarness/` files changed; no production file was touched).

## Handoff

A future implementation card builds the translator into production
(`Runtime/Execution/Burst/Dispatch/` or `Authoring/Registry/Generated/`, per the ADR's own decided
location constraints) per this ADR's now-proven case/field mapping and encoding name-mapping, and
widens `P6-009`'s `test-node` to actually drive generated dispatch -- accounting explicitly for the
dispatch-index-contiguity finding when the target node is not already at index 0 in its real
catalog (the common case once a project has more than one custom node). `Registered`-encoded fields
and the `AsyncOperation`/`Completion` binding pair remain open research for that future card.
