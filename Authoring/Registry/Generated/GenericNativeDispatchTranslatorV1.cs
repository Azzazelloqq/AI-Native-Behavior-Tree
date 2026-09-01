using System;
using System.Linq;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;

namespace AIBT.Authoring
{
    // Applies ADR-P6-022 to production (P7-009): builds a NativeBurstDispatchWorkspaceShapeV2
    // purely from a shard's own AibtGeneratedMetadata.CanonicalDescriptorJson (via the already-
    // accepted GeneratedShardMetadataMaterializer), for a target node that may not be first in its
    // own catalog. NativeBurstDispatchWorkspaceOwnerV2.TryCreate requires a workspace's Cases array
    // to be positionally self-consistent from index 0 (Cases[i].CatalogCaseIndex == i) -- reaching a
    // node that isn't first therefore requires translating the full 0..targetIndex case prefix, not
    // an isolated case (ADR-P6-022's own spike addendum finding). Every case in the prefix, not only
    // the target, must fall inside the proven scope (built-in-typed fields, non-async/non-Completion
    // bindings, no Registered/FixedBytes storage) -- an out-of-scope case anywhere in the prefix
    // fails translation with a structured reason naming which case blocked it.
    internal static class GenericNativeDispatchTranslatorV1
    {
        internal readonly struct BuiltShape : IDisposable
        {
            internal BuiltShape(
                NativeBurstDispatchWorkspaceShapeV2 shape,
                NativeArray<NativeBurstDispatchCaseV2> cases,
                NativeArray<NativeBurstDispatchFieldV2> configurationFields,
                NativeArray<NativeBurstDispatchFieldV2> memoryFields,
                NativeArray<NativeBurstDispatchBindingV2> bindings,
                NativeArray<NativeBurstDispatchFieldV2> valueFields,
                NativeArray<NativeBurstDispatchCanonicalRangeV2> caseRanges,
                NativeArray<NativeBurstDispatchCanonicalRangeV2> bindingRanges,
                NativeArray<NativeBurstDispatchCanonicalRuleV2> rules,
                uint targetCaseIndex)
            {
                Shape = shape;
                _cases = cases;
                _configurationFields = configurationFields;
                _memoryFields = memoryFields;
                _bindings = bindings;
                _valueFields = valueFields;
                _caseRanges = caseRanges;
                _bindingRanges = bindingRanges;
                _rules = rules;
                TargetCaseIndex = targetCaseIndex;
            }

            internal NativeBurstDispatchWorkspaceShapeV2 Shape { get; }
            internal uint TargetCaseIndex { get; }

            private readonly NativeArray<NativeBurstDispatchCaseV2> _cases;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _configurationFields;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _memoryFields;
            private readonly NativeArray<NativeBurstDispatchBindingV2> _bindings;
            private readonly NativeArray<NativeBurstDispatchFieldV2> _valueFields;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _caseRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRangeV2> _bindingRanges;
            private readonly NativeArray<NativeBurstDispatchCanonicalRuleV2> _rules;

            internal NativeBurstDispatchCaseV2 TargetCase => _cases[(int)TargetCaseIndex];

            public void Dispose()
            {
                _rules.Dispose();
                _bindingRanges.Dispose();
                _caseRanges.Dispose();
                _valueFields.Dispose();
                _bindings.Dispose();
                _memoryFields.Dispose();
                _configurationFields.Dispose();
                _cases.Dispose();
            }
        }

        // catalogCaseIndex is derived from the node's own ordinal position within the materialized,
        // canonically-sorted artifact (GeneratedShardMetadataMaterializer sorts by TypeId then
        // Version, matching GeneratedMetadataEmitter's own emission order exactly) -- proven correct
        // for a single-shard catalog, where the shard's own node order equals the facade's case
        // order; a multi-shard catalog's cross-shard ordering is not addressed here.
        internal static BuiltShape Build(
            GeneratedShardMetadataArtifact artifact,
            string targetTypeId,
            BurstCatalogHandshake handshake,
            Allocator allocator)
        {
            var targetIndex = -1;
            for (var index = 0; index < artifact.Nodes.Count; index++)
            {
                if (artifact.Nodes[index].Manifest.TypeId != targetTypeId) continue;
                targetIndex = index;
                break;
            }
            if (targetIndex < 0) throw new ArgumentException("No node with type ID '" + targetTypeId + "' in the materialized artifact.", nameof(targetTypeId));

            var caseCount = targetIndex + 1;
            for (var index = 0; index < caseCount; index++)
            {
                RequireInScope(artifact.Nodes[index]);
            }

            var totalConfigurationFields = 0;
            var totalMemoryFields = 0;
            var totalBindings = 0;
            for (var index = 0; index < caseCount; index++)
            {
                var node = artifact.Nodes[index];
                totalConfigurationFields += node.Configuration.Count;
                totalMemoryFields += node.Memory.Count;
                totalBindings += node.Bindings.Count;
            }

            var configurationFields = new NativeArray<NativeBurstDispatchFieldV2>(totalConfigurationFields, allocator);
            var memoryFields = new NativeArray<NativeBurstDispatchFieldV2>(totalMemoryFields, allocator);
            var bindings = new NativeArray<NativeBurstDispatchBindingV2>(totalBindings, allocator);
            var valueFields = new NativeArray<NativeBurstDispatchFieldV2>(totalBindings, allocator);
            var cases = new NativeArray<NativeBurstDispatchCaseV2>(caseCount, allocator);

            var configCursor = 0u;
            var memoryCursor = 0u;
            var bindingCursor = 0u;
            for (var caseIndex = 0; caseIndex < caseCount; caseIndex++)
            {
                var node = artifact.Nodes[caseIndex];

                for (var fieldIndex = 0; fieldIndex < node.Configuration.Count; fieldIndex++)
                    configurationFields[(int)configCursor + fieldIndex] = TranslateField((uint)fieldIndex, node.Configuration[fieldIndex]);

                for (var fieldIndex = 0; fieldIndex < node.Memory.Count; fieldIndex++)
                    memoryFields[(int)memoryCursor + fieldIndex] = TranslateField((uint)fieldIndex, node.Memory[fieldIndex]);

                for (var bindingIndex = 0; bindingIndex < node.Bindings.Count; bindingIndex++)
                {
                    var binding = node.Bindings[bindingIndex];
                    var type = binding.Types[0];
                    var valueEncoding = TranslateEncoding(BuiltInEncodingOf(type.CanonicalTypeId));
                    valueFields[(int)bindingCursor + bindingIndex] =
                        new NativeBurstDispatchFieldV2(0u, 0u, 1u, BuiltInSizeOf(type.CanonicalTypeId), valueEncoding);
                    var configurationFieldOrdinal = (uint)FindConfigurationOrdinal(node, binding.BindingId);
                    var scope = binding.Kind <= GeneratedBindingKind.BlackboardReadWrite
                        ? (byte)binding.Scope
                        : NativeBurstDispatchBindingV2.NoScope;
                    bindings[(int)bindingCursor + bindingIndex] = new NativeBurstDispatchBindingV2(
                        (uint)bindingIndex,
                        configurationFieldOrdinal,
                        TranslateBindingKind(binding.Kind),
                        scope,
                        NativeBurstDispatchBindingPhaseMaskV2.None,
                        type.NumericTypeId,
                        type.Version,
                        bindingCursor + (uint)bindingIndex,
                        1u,
                        BuiltInSizeOf(type.CanonicalTypeId),
                        0UL, 0u, 0u, 0u, 0u);
                }

                cases[caseIndex] = new NativeBurstDispatchCaseV2(
                    StableHash.Fnv1A64(node.Manifest.TypeId),
                    node.Manifest.Version,
                    (uint)caseIndex,
                    configCursor, (uint)node.Configuration.Count, node.Manifest.Configuration.Size,
                    memoryCursor, (uint)node.Memory.Count, node.Manifest.Memory.Size,
                    TranslatePhases(node.CallbackCapabilities),
                    TranslateStatuses(node.Manifest.PossibleStatuses),
                    node.HasRandomStream,
                    bindingCursor, (uint)node.Bindings.Count);

                configCursor += (uint)node.Configuration.Count;
                memoryCursor += (uint)node.Memory.Count;
                bindingCursor += (uint)node.Bindings.Count;
            }

            // NativeBurstDispatchBindingValidationV2.ValidateShapeMetadata requires exactly two
            // canonical ranges per case (configuration, memory) and two per binding (primary,
            // secondary) -- not one. All are zero-length since this translator's proven scope has
            // no FixedBytes/canonical-rule fields.
            var caseRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(cases.Length * 2, allocator);
            for (var index = 0; index < caseRanges.Length; index++)
                caseRanges[index] = new NativeBurstDispatchCanonicalRangeV2(0u, 0u);
            var bindingRanges = new NativeArray<NativeBurstDispatchCanonicalRangeV2>(bindings.Length * 2, allocator);
            for (var index = 0; index < bindingRanges.Length; index++)
                bindingRanges[index] = new NativeBurstDispatchCanonicalRangeV2(0u, 0u);
            var rules = new NativeArray<NativeBurstDispatchCanonicalRuleV2>(0, allocator);
            var canonicalInput = new NativeBurstDispatchCanonicalInputV2(
                caseRanges.AsReadOnly(), bindingRanges.AsReadOnly(), rules.AsReadOnly());

            var shape = new NativeBurstDispatchWorkspaceShapeV2(
                handshake,
                cases.AsReadOnly(),
                configurationFields.AsReadOnly(),
                memoryFields.AsReadOnly(),
                bindings.AsReadOnly(),
                valueFields.AsReadOnly(),
                canonicalInput);

            return new BuiltShape(
                shape, cases, configurationFields, memoryFields, bindings, valueFields,
                caseRanges, bindingRanges, rules, (uint)targetIndex);
        }

        private static void RequireInScope(GeneratedNodeDescriptor node)
        {
            if (node.Bindings.Any(binding =>
                    binding.Kind == GeneratedBindingKind.AsyncOperation
                    || binding.Kind == GeneratedBindingKind.Completion
                    || binding.Types.Count != 1
                    || binding.Types[0].Role != GeneratedTypeRole.Value
                    || binding.Types[0].RegisteredDescriptor.IsValid))
                throw new NotSupportedException(
                    "Node '" + node.Manifest.TypeId + "' uses an Async/Completion binding or a Registered value type, outside this translator's proven scope (ADR-P6-022).");
            if (node.Configuration.Any(field =>
                    field.Encoding == GeneratedFieldEncoding.FixedBytes || field.Encoding == GeneratedFieldEncoding.Registered)
                || node.Memory.Any(field =>
                    field.Encoding == GeneratedFieldEncoding.FixedBytes || field.Encoding == GeneratedFieldEncoding.Registered))
                throw new NotSupportedException(
                    "Node '" + node.Manifest.TypeId + "' uses a FixedBytes/Registered storage field, outside this translator's proven scope (ADR-P6-022).");
        }

        private static int FindConfigurationOrdinal(GeneratedNodeDescriptor node, string bindingId)
        {
            for (var index = 0; index < node.Configuration.Count; index++)
                if (node.Configuration[index].BindingId == bindingId) return index;
            throw new ArgumentException("No configuration field is bound to binding '" + bindingId + "'.");
        }

        private static NativeBurstDispatchFieldV2 TranslateField(uint fieldOrdinal, GeneratedStorageField field)
            => new NativeBurstDispatchFieldV2(fieldOrdinal, field.Offset, 1u, field.Size, TranslateEncoding(field.Encoding));

        // Name-to-name mapping only, never a numeric cast: GeneratedFieldEncoding and
        // NativeBurstDispatchFieldEncodingV2 disagree on GeneratedHandle's own numeric value
        // (12 vs. 11) and have no dispatch-side value at all for FixedBytes/Registered (ADR-P6-022
        // finding 2).
        private static NativeBurstDispatchFieldEncodingV2 TranslateEncoding(GeneratedFieldEncoding encoding)
        {
            switch (encoding)
            {
                case GeneratedFieldEncoding.Bool8: return NativeBurstDispatchFieldEncodingV2.Boolean;
                case GeneratedFieldEncoding.Int8: return NativeBurstDispatchFieldEncodingV2.Int8;
                case GeneratedFieldEncoding.UInt8: return NativeBurstDispatchFieldEncodingV2.UInt8;
                case GeneratedFieldEncoding.Int16LE: return NativeBurstDispatchFieldEncodingV2.Int16;
                case GeneratedFieldEncoding.UInt16LE: return NativeBurstDispatchFieldEncodingV2.UInt16;
                case GeneratedFieldEncoding.Int32LE: return NativeBurstDispatchFieldEncodingV2.Int32;
                case GeneratedFieldEncoding.UInt32LE: return NativeBurstDispatchFieldEncodingV2.UInt32;
                case GeneratedFieldEncoding.Int64LE: return NativeBurstDispatchFieldEncodingV2.Int64;
                case GeneratedFieldEncoding.UInt64LE: return NativeBurstDispatchFieldEncodingV2.UInt64;
                case GeneratedFieldEncoding.Float32BitsLE: return NativeBurstDispatchFieldEncodingV2.Float32;
                case GeneratedFieldEncoding.Float64BitsLE: return NativeBurstDispatchFieldEncodingV2.Float64;
                case GeneratedFieldEncoding.GeneratedHandle: return NativeBurstDispatchFieldEncodingV2.GeneratedHandle;
                default:
                    throw new NotSupportedException(
                        "Field encoding '" + encoding + "' is outside this translator's proven scope (ADR-P6-022).");
            }
        }

        // The two binding-kind vocabularies share the same ordinal layout (BlackboardRead=0 ...
        // Completion=6), unlike the field-encoding vocabularies; still mapped by name, never by an
        // unchecked cast, so a future divergence fails loudly instead of silently misrouting.
        private static NativeBurstDispatchBindingKindV2 TranslateBindingKind(GeneratedBindingKind kind)
        {
            switch (kind)
            {
                case GeneratedBindingKind.BlackboardRead: return NativeBurstDispatchBindingKindV2.BlackboardRead;
                case GeneratedBindingKind.BlackboardWrite: return NativeBurstDispatchBindingKindV2.BlackboardWrite;
                case GeneratedBindingKind.BlackboardReadWrite: return NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
                case GeneratedBindingKind.SnapshotRead: return NativeBurstDispatchBindingKindV2.SnapshotRead;
                case GeneratedBindingKind.EffectCommand: return NativeBurstDispatchBindingKindV2.EffectCommand;
                case GeneratedBindingKind.AsyncOperation: return NativeBurstDispatchBindingKindV2.AsyncOperation;
                case GeneratedBindingKind.Completion: return NativeBurstDispatchBindingKindV2.Completion;
                default: throw new NotSupportedException("Unknown binding kind '" + kind + "'.");
            }
        }

        // CallbackCapabilities (0x0f / 0x1f, from AibtBurstNode's own closed ABI) and
        // NativeBurstDispatchPhaseMaskV2 (Enter=1,Tick=2,Abort=4,Exit=8,Observer=16) share the
        // identical bit layout by construction (both derive from the same burst-node-abi-v1.md
        // closed set) -- direct bit test per flag, not a raw cast, so a future divergence throws
        // instead of silently misrouting.
        private static NativeBurstDispatchPhaseMaskV2 TranslatePhases(byte callbackCapabilities)
        {
            const byte known = 0x1f;
            if ((callbackCapabilities & ~known) != 0)
                throw new NotSupportedException("Unknown callback capability bits in '" + callbackCapabilities + "'.");
            var phases = NativeBurstDispatchPhaseMaskV2.None;
            if ((callbackCapabilities & 0x01) != 0) phases |= NativeBurstDispatchPhaseMaskV2.Enter;
            if ((callbackCapabilities & 0x02) != 0) phases |= NativeBurstDispatchPhaseMaskV2.Tick;
            if ((callbackCapabilities & 0x04) != 0) phases |= NativeBurstDispatchPhaseMaskV2.Abort;
            if ((callbackCapabilities & 0x08) != 0) phases |= NativeBurstDispatchPhaseMaskV2.Exit;
            if ((callbackCapabilities & 0x10) != 0) phases |= NativeBurstDispatchPhaseMaskV2.Observer;
            return phases;
        }

        private static BurstNodeStatusMask TranslateStatuses(System.Collections.Generic.IReadOnlyList<NodeStatus> statuses)
        {
            var mask = BurstNodeStatusMask.None;
            foreach (var status in statuses)
            {
                switch (status)
                {
                    case NodeStatus.Success: mask |= BurstNodeStatusMask.Success; break;
                    case NodeStatus.Failure: mask |= BurstNodeStatusMask.Failure; break;
                    case NodeStatus.Running: mask |= BurstNodeStatusMask.Running; break;
                    default: throw new NotSupportedException("Unknown node status '" + status + "'.");
                }
            }
            return mask;
        }

        private static uint BuiltInSizeOf(string canonicalTypeId)
        {
            switch (canonicalTypeId)
            {
                case "Bool": case "Int8": case "UInt8": return 1u;
                case "Int16": case "UInt16": return 2u;
                case "Int32": case "UInt32": case "Float32": return 4u;
                case "Int64": case "UInt64": case "Float64": return 8u;
                default: throw new NotSupportedException("Value type '" + canonicalTypeId + "' is outside this translator's proven scope (ADR-P6-022).");
            }
        }

        private static GeneratedFieldEncoding BuiltInEncodingOf(string canonicalTypeId)
        {
            switch (canonicalTypeId)
            {
                case "Bool": return GeneratedFieldEncoding.Bool8;
                case "Int8": return GeneratedFieldEncoding.Int8;
                case "UInt8": return GeneratedFieldEncoding.UInt8;
                case "Int16": return GeneratedFieldEncoding.Int16LE;
                case "UInt16": return GeneratedFieldEncoding.UInt16LE;
                case "Int32": return GeneratedFieldEncoding.Int32LE;
                case "UInt32": return GeneratedFieldEncoding.UInt32LE;
                case "Int64": return GeneratedFieldEncoding.Int64LE;
                case "UInt64": return GeneratedFieldEncoding.UInt64LE;
                case "Float32": return GeneratedFieldEncoding.Float32BitsLE;
                case "Float64": return GeneratedFieldEncoding.Float64BitsLE;
                default: throw new NotSupportedException("Value type '" + canonicalTypeId + "' is outside this translator's proven scope (ADR-P6-022).");
            }
        }
    }
}
