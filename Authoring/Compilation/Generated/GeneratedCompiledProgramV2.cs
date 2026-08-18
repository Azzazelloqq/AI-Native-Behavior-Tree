using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AIBT.Authoring
{
    public enum GeneratedAccessModeV2 : byte { Read = 1, Write = 2, ReadWrite = 3 }

    public sealed class GeneratedCompiledAccessRecordV2
    {
        internal GeneratedCompiledAccessRecordV2(uint nodeIndex, uint ordinal, BlackboardScope scope, uint slotIndex, GeneratedAccessModeV2 mode, BlackboardReductionKind reduction, string bindingId)
        {
            NodeIndex = nodeIndex; AccessOrdinal = ordinal; Scope = scope; SlotIndex = slotIndex; Mode = mode; Reduction = reduction; BindingId = bindingId;
        }
        public uint NodeIndex { get; }
        public uint AccessOrdinal { get; }
        public BlackboardScope Scope { get; }
        public uint SlotIndex { get; }
        public GeneratedAccessModeV2 Mode { get; }
        public BlackboardReductionKind Reduction { get; }
        internal string BindingId { get; }
    }

    public sealed class GeneratedCompiledSlotRecordV2
    {
        internal GeneratedCompiledSlotRecordV2(GeneratedScopeSlot slot, uint defaultOffset, CompiledBlackboardAccessFlags accessFlags)
        { Slot = slot; DefaultOffset = defaultOffset; AccessFlags = accessFlags; }
        public GeneratedScopeSlot Slot { get; }
        public uint DefaultOffset { get; }
        public CompiledBlackboardAccessFlags AccessFlags { get; }
    }

    public readonly struct GeneratedWatchedSlotV2
    {
        internal GeneratedWatchedSlotV2(BlackboardScope scope, uint slotIndex) { Scope = scope; SlotIndex = slotIndex; }
        public BlackboardScope Scope { get; }
        public uint SlotIndex { get; }
    }

    public sealed class GeneratedCompiledProgramV2
    {
        public const uint AgentScopeCapability = 1u << 7;
        public const uint SharedScopeCapability = 1u << 8;
        private readonly byte[] _bytes;
        private readonly ReadOnlyCollection<GeneratedCompiledAccessRecordV2> _accesses;
        private readonly ReadOnlyCollection<GeneratedCompiledSlotRecordV2> _slots;
        private readonly ReadOnlyCollection<GeneratedWatchedSlotV2> _watched;
        private readonly byte[] _configBlob;
        private readonly byte[] _defaultBlob;

        internal GeneratedCompiledProgramV2(CompiledProgram semanticProgram, GeneratedScopeCompilationResult scopes,
            IList<GeneratedCompiledAccessRecordV2> accesses, IList<GeneratedCompiledSlotRecordV2> slots,
            IList<GeneratedWatchedSlotV2> watched, byte[] configBlob, byte[] defaultBlob)
        {
            if (semanticProgram == null) throw new ArgumentNullException(nameof(semanticProgram));
            SemanticProgram = WithContentHash(semanticProgram, CompiledProgramContentHashV1.Compute(semanticProgram));
            if (CompiledProgramContentHashV1.Compute(SemanticProgram) != SemanticProgram.Header.CompiledContentHash)
                throw new InvalidOperationException("The inner semantic program must retain its exact v1 content hash.");
            Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _accesses = new List<GeneratedCompiledAccessRecordV2>(accesses).AsReadOnly();
            _slots = new List<GeneratedCompiledSlotRecordV2>(slots).AsReadOnly();
            _watched = new List<GeneratedWatchedSlotV2>(watched).AsReadOnly();
            _configBlob = (byte[])configBlob.Clone();
            _defaultBlob = (byte[])defaultBlob.Clone();
            var preimage = GeneratedCompiledProgramV2Serializer.Serialize(this, _configBlob, _defaultBlob);
            ContentHash = new CompiledHash(StableHash.Sha256Hex(preimage));
            _bytes = GeneratedCompiledProgramV2Serializer.Serialize(this, _configBlob, _defaultBlob);
            if (!Equal(preimage, _bytes))
                throw new InvalidOperationException("The compiled-v2 hash field must be excluded from its exact preimage stream.");
        }

        public uint CompiledFormatVersion => 2;
        public CompiledProgram SemanticProgram { get; private set; }
        public GeneratedScopeCompilationResult Scopes { get; }
        public IReadOnlyList<GeneratedCompiledAccessRecordV2> Accesses => _accesses;
        public IReadOnlyList<GeneratedCompiledSlotRecordV2> Slots => _slots;
        public IReadOnlyList<GeneratedWatchedSlotV2> WatchedSlots => _watched;
        public CompiledHash ContentHash { get; }
        public byte[] GetBytesCopy() => (byte[])_bytes.Clone();
        public byte[] GetConfigBlobCopy() => (byte[])_configBlob.Clone();
        public byte[] GetDefaultValueBlobCopy() => (byte[])_defaultBlob.Clone();
        public NativeCompiledProgramHeaderV1 CreateNativeHeaderProjection()
            => new NativeCompiledProgramHeaderV1(WithContentHash(SemanticProgram, ContentHash).Header);

        public NativeProgramBlackboardBindingV2 CreateNativeBlackboardBindingV2(
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            if (registeredTypes == null) throw new ArgumentNullException(nameof(registeredTypes));

            var scopes = new List<NativeBlackboardScopeBindingV2>(Scopes.Descriptors.Count);
            for (var index = 0; index < Scopes.Descriptors.Count; index++)
            {
                var scope = Scopes.Descriptors[index];
                scopes.Add(new NativeBlackboardScopeBindingV2(
                    scope.Scope, scope.Contract.ContractId, scope.Contract.ContractVersion,
                    scope.FirstSlot, scope.SlotCount, scope.GetSchemaBytesCopy(),
                    scope.GetRawLayoutCopy()));
            }

            var typeIndex = new Dictionary<string, uint>(StringComparer.Ordinal);
            var types = new List<NativeRegisteredBlackboardTypeBindingV2>(registeredTypes.Entries.Count);
            var fields = new List<NativeRegisteredBlackboardFieldBindingV2>();
            for (var index = 0; index < registeredTypes.Entries.Count; index++)
            {
                var entry = registeredTypes.Entries[index];
                var firstField = checked((uint)fields.Count);
                for (var fieldIndex = 0; fieldIndex < entry.Fields.Count; fieldIndex++)
                {
                    var field = entry.Fields[fieldIndex];
                    fields.Add(new NativeRegisteredBlackboardFieldBindingV2(
                        field.NumericFieldId, StableHash.Fnv1A64(field.ValueTypeId), field.ValueTypeVersion,
                        field.Offset, field.Size, field.Alignment,
                        (NativeBlackboardFieldEncodingV2)field.Encoding,
                        field.RegisteredDescriptor.CanonicalSchemaId,
                        new CompiledHash(field.RegisteredSchemaHash),
                        field.RegisteredDescriptor.EqualityContractId));
                }
                types.Add(new NativeRegisteredBlackboardTypeBindingV2(
                    entry.Descriptor, GeneratedBlackboardContractBytesV2.RegisteredSchema(entry), firstField,
                    checked((uint)entry.Fields.Count)));
                typeIndex.Add(TypeIdentity(entry.Descriptor.TypeId, entry.Descriptor.Version), (uint)index);
            }

            var slots = new List<NativeBlackboardSlotBindingV2>(_slots.Count);
            var slotAuthorities = new List<NativeBlackboardSlotAuthorityV2>(_slots.Count);
            for (var index = 0; index < _slots.Count; index++)
            {
                var source = _slots[index];
                var slot = source.Slot;
                var layout = slot.Key.Type.RuntimeDescriptor;
                var scopeIndex = CompiledIndex.Invalid;
                for (var scopeOrdinal = 0; scopeOrdinal < Scopes.Descriptors.Count; scopeOrdinal++)
                    if (Scopes.Descriptors[scopeOrdinal].Scope == slot.Key.Scope) { scopeIndex = (uint)scopeOrdinal; break; }
                var registeredIndex = CompiledIndex.Invalid;
                if (slot.Key.Type.IsRegistered)
                {
                    if (!typeIndex.TryGetValue(TypeIdentity(layout.TypeId, layout.Version), out registeredIndex)
                        || registeredTypes.Entries[(int)registeredIndex].Descriptor != slot.Key.Type.RegisteredDescriptor)
                        throw new InvalidOperationException("Compiled registered slot is absent from the exact accepted native catalog.");
                }
                slots.Add(new NativeBlackboardSlotBindingV2(
                    StableHash.Fnv1A64(slot.Key.Id), layout.TypeId, layout.Version,
                    slot.Key.Type.EnumContractId, slot.Key.Scope, slot.SlotIndex, slot.Offset,
                    checked((uint)layout.Size), checked((uint)layout.Alignment), source.DefaultOffset,
                    checked((uint)(slot.DefaultBytes?.Length ?? 0)), source.AccessFlags,
                    scopeIndex, registeredIndex, (NativeBlackboardReductionKindV2)slot.Key.Reduction));
                slotAuthorities.Add(new NativeBlackboardSlotAuthorityV2(
                    slot.Key.Id, slot.Key.Type.CanonicalTypeId, slot.Key.Type.EnumContract,
                    GeneratedBlackboardContractBytesV2.CanonicalDefault(slot.Key)));
            }

            var accesses = new List<NativeBlackboardAccessBindingV2>(_accesses.Count);
            for (var index = 0; index < _accesses.Count; index++)
            {
                var source = _accesses[index];
                NativeBlackboardSlotBindingV2 slot = default;
                var found = false;
                for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    if (slots[slotIndex].Scope != source.Scope || slots[slotIndex].ScopeSlotIndex != source.SlotIndex) continue;
                    if (found) throw new InvalidOperationException("Compiled access scope-local slot mapping is ambiguous.");
                    slot = slots[slotIndex];
                    found = true;
                }
                if (!found) throw new InvalidOperationException("Compiled access references an invalid scope-local slot.");
                accesses.Add(new NativeBlackboardAccessBindingV2(
                    source.NodeIndex, source.AccessOrdinal, source.Scope, source.SlotIndex,
                    source.Mode == GeneratedAccessModeV2.Read ? NativeBlackboardAccessModeV2.Read
                        : source.Mode == GeneratedAccessModeV2.Write ? NativeBlackboardAccessModeV2.Write
                        : NativeBlackboardAccessModeV2.ReadWrite,
                    slot.TypeId, slot.TypeVersion, slot.EnumContractId, slot.RegisteredTypeIndex,
                    (NativeBlackboardReductionKindV2)source.Reduction));
            }

            var watched = new List<NativeBlackboardWatchedSlotBindingV2>(_watched.Count);
            for (var index = 0; index < _watched.Count; index++)
                watched.Add(new NativeBlackboardWatchedSlotBindingV2(_watched[index].Scope, _watched[index].SlotIndex));

            return new NativeProgramBlackboardBindingV2(
                SemanticProgram, GetBytesCopy(), scopes, slots, slotAuthorities, accesses, watched, types, fields);
        }

        private static CompiledProgram WithContentHash(CompiledProgram source, CompiledHash hash)
        {
            var value = source.Header;
            var header = new CompiledProgramHeader(
                value.CompiledFormatVersion, value.ExecutionSemanticsVersion, value.CompilerVersion,
                value.CanonicalSemanticHash, value.NodeRegistryHash, value.CanonicalPolicyHash,
                value.PolicyFormatVersion, hash, value.RootNodeIndex, value.NodeCount,
                value.ChildIndexCount, value.BlackboardSlotCount, value.DebugMapCount,
                value.ConfigBlobSize, value.InstanceNodeMemorySize, value.RequiredMaximumAlignment,
                value.CapabilityFlags, value.DeterministicModeCompatible);
            return new CompiledProgram(header, source.Nodes, source.ChildIndices, source.ReadSlotIndices,
                source.WriteSlotIndices, source.BlackboardSlots, source.Observers,
                source.WatchedSlotIndices, source.ConfigBlob, source.DefaultValueBlob, source.DebugMap);
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        private static string TypeIdentity(ulong typeId, uint version)
            => typeId.ToString("x16", CultureInfo.InvariantCulture) + "\0" + version.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class GeneratedCompiledProgramV2Result
    {
        internal GeneratedCompiledProgramV2Result(GeneratedCompiledProgramV2 program, DiagnosticCollection diagnostics)
        { Program = program; Diagnostics = diagnostics; }
        public GeneratedCompiledProgramV2 Program { get; }
        public DiagnosticCollection Diagnostics { get; }
        public bool Success => Program != null;
    }

    public static class GeneratedCompiledProgramV2Compiler
    {
        public static GeneratedCompiledProgramV2Result Compile(TreeDocument document, IEnumerable<GeneratedNodeDescriptor> descriptors, ReferenceCompilerOptions options)
            => Compile(document, descriptors, null, options);

        public static GeneratedCompiledProgramV2Result Compile(TreeDocument document, IEnumerable<GeneratedNodeDescriptor> descriptors,
            RegisteredBlackboardTypeCatalog registeredTypes, ReferenceCompilerOptions options)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));
            var generated = new List<GeneratedNodeDescriptor>(descriptors);
            generated.Sort((left, right) =>
            {
                var comparison = Utf8OrdinalComparer.Instance.Compare(left.Manifest.TypeId, right.Manifest.TypeId);
                return comparison != 0 ? comparison : left.Manifest.Version.CompareTo(right.Manifest.Version);
            });
            var diagnostics = new List<Diagnostic>();
            if (options == null || options.Policy == null)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "Generated compiled format v2 requires explicit reference compiler options and policy."));
                return Failure(diagnostics);
            }
            if (document.FormatVersion != TreeDocument.LatestFormatVersion)
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(ReferenceCompilerDiagnosticCodes.MissingScopeContract, "Generated compiled format v2 requires tree format version 2.", options?.SourceId, "/formatVersion", treeId: document.TreeId));
            var registryResult = GeneratedNodeRegistry.Build(generated, includeBuiltIns: true);
            Add(diagnostics, registryResult.Diagnostics);
            if (registryResult.Success)
            {
                var accepted = options.Policy.CreateValidationOptions(options.SourceId);
                Add(diagnostics, TreeValidator.Validate(document, registryResult.Registry,
                    new ValidationOptions(options.SourceId, accepted.UnreachableNodes, true, true, accepted.Policy), registeredTypes));
            }
            var scopeResult = GeneratedScopeCompiler.Compile(document, generated, registeredTypes, options.SourceId);
            Add(diagnostics, scopeResult.Diagnostics);
            if (HasError(diagnostics)) return Failure(diagnostics);
            var semanticDocument = CreateSemanticProjection(document, generated);
            var projectionRegistry = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            Add(diagnostics, projectionRegistry.Diagnostics);
            if (!projectionRegistry.Success || HasError(diagnostics)) return Failure(diagnostics);
            var semantic = ReferenceCompiler.Compile(semanticDocument, projectionRegistry.Registry, options);
            Add(diagnostics, semantic.Diagnostics);
            if (!semantic.Success || HasError(diagnostics)) return Failure(diagnostics);
            try
            {
                var finalSemantic = BuildFinalSemanticProgram(document, semantic.Program, registryResult.Registry, generated, scopeResult, options, registeredTypes);
                var slotsById = IndexSlots(scopeResult.Slots);
                var accesses = BuildAccesses(document, finalSemantic, generated, scopeResult, slotsById);
                var config = BuildConfigBlob(finalSemantic, generated, scopeResult, accesses);
                var watched = BuildWatched(finalSemantic, scopeResult.Slots);
                var slotRecords = BuildSlots(scopeResult.Slots, accesses, watched, out var defaults);
                return new GeneratedCompiledProgramV2Result(
                    new GeneratedCompiledProgramV2(finalSemantic, scopeResult, accesses, slotRecords, watched, config, defaults),
                    new DiagnosticCollection(diagnostics));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure,
                    "Generated compiled v2 construction failed: " + exception.Message, options?.SourceId, treeId: document.TreeId));
                return Failure(diagnostics);
            }
        }

        private static List<GeneratedCompiledAccessRecordV2> BuildAccesses(TreeDocument document, CompiledProgram program,
            IList<GeneratedNodeDescriptor> descriptors, GeneratedScopeCompilationResult scopes, IDictionary<string, GeneratedScopeSlot> slots)
        {
            var descriptorMap = new Dictionary<string, GeneratedNodeDescriptor>(StringComparer.Ordinal);
            for (var index = 0; index < descriptors.Count; index++) descriptorMap.Add(Identity(descriptors[index].Manifest.TypeId, descriptors[index].Manifest.Version), descriptors[index]);
            var documentNodes = new Dictionary<NodeId, NodeDocument>();
            for (var index = 0; index < document.Nodes.Count; index++) documentNodes.Add(document.Nodes[index].Id, document.Nodes[index]);
            var generatedByNode = new Dictionary<NodeId, List<GeneratedNodeAccessRecord>>();
            for (var index = 0; index < scopes.Accesses.Count; index++)
            {
                var item = scopes.Accesses[index];
                if (!generatedByNode.TryGetValue(item.NodeId, out var list)) generatedByNode.Add(item.NodeId, list = new List<GeneratedNodeAccessRecord>());
                list.Add(item);
            }
            foreach (var pair in generatedByNode) pair.Value.Sort((left, right) => left.Binding.Ordinal.CompareTo(right.Binding.Ordinal));
            var result = new List<GeneratedCompiledAccessRecordV2>();
            for (var nodeIndex = 0; nodeIndex < program.DebugMap.Count; nodeIndex++)
            {
                var nodeId = program.DebugMap[nodeIndex].AuthoringNodeId;
                var node = documentNodes[nodeId];
                var pending = new List<GeneratedCompiledAccessRecordV2>();
                var semanticNode = program.Nodes[nodeIndex];
                AddProjectedAccesses(pending, program.ReadSlotIndices, semanticNode.ReadSlots, document, slots, (uint)nodeIndex, GeneratedAccessModeV2.Read);
                AddProjectedAccesses(pending, program.WriteSlotIndices, semanticNode.WriteSlots, document, slots, (uint)nodeIndex, GeneratedAccessModeV2.Write);
                if (descriptorMap.ContainsKey(Identity(node.TypeId, (uint)node.TypeVersion))
                    && generatedByNode.TryGetValue(nodeId, out var generatedAccesses))
                {
                    for (var index = 0; index < generatedAccesses.Count; index++)
                    {
                        var access = generatedAccesses[index];
                        var mode = access.Binding.Kind == GeneratedBindingKind.BlackboardRead ? GeneratedAccessModeV2.Read
                            : access.Binding.Kind == GeneratedBindingKind.BlackboardWrite ? GeneratedAccessModeV2.Write : GeneratedAccessModeV2.ReadWrite;
                        pending.Add(new GeneratedCompiledAccessRecordV2((uint)nodeIndex, 0, access.Scope, access.ScopeSlot, mode,
                            mode == GeneratedAccessModeV2.Read ? BlackboardReductionKind.None : access.Reduction, access.Binding.BindingId));
                    }
                }
                uint ordinal = 0;
                foreach (var access in OrderedAccesses(pending))
                    result.Add(new GeneratedCompiledAccessRecordV2(access.NodeIndex, ordinal++, access.Scope, access.SlotIndex, access.Mode, access.Reduction, access.BindingId));
            }
            return result;
        }

        private static void AddProjectedAccesses(ICollection<GeneratedCompiledAccessRecordV2> result,
            IReadOnlyList<uint> semanticSlotIndices, CompiledRange range, TreeDocument document,
            IDictionary<string, GeneratedScopeSlot> slots, uint nodeIndex, GeneratedAccessModeV2 mode)
        {
            var canonicalKeys = new List<BlackboardKeyDefinition>(document.Blackboard);
            canonicalKeys.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id, right.Id));
            for (var index = range.Offset; index < range.EndExclusive; index++)
            {
                var semanticSlot = semanticSlotIndices[(int)index];
                AddLiteral(result, nodeIndex, slots[canonicalKeys[(int)semanticSlot].Id], mode);
            }
        }

        private static void AddLiteral(ICollection<GeneratedCompiledAccessRecordV2> result, uint nodeIndex, GeneratedScopeSlot slot, GeneratedAccessModeV2 mode)
            => result.Add(new GeneratedCompiledAccessRecordV2(nodeIndex, 0, slot.Key.Scope, slot.SlotIndex, mode,
                mode == GeneratedAccessModeV2.Read ? BlackboardReductionKind.None : slot.Key.Reduction, null));

        private static IEnumerable<GeneratedCompiledAccessRecordV2> OrderedAccesses(IList<GeneratedCompiledAccessRecordV2> values)
        {
            foreach (var mode in new[] { GeneratedAccessModeV2.Read, GeneratedAccessModeV2.ReadWrite, GeneratedAccessModeV2.Write })
            {
                var selected = new List<GeneratedCompiledAccessRecordV2>();
                for (var index = 0; index < values.Count; index++) if (values[index].Mode == mode) selected.Add(values[index]);
                selected.Sort((left, right) =>
                {
                    var comparison = left.Scope.CompareTo(right.Scope);
                    if (comparison != 0) return comparison;
                    comparison = left.SlotIndex.CompareTo(right.SlotIndex);
                    if (comparison != 0) return comparison;
                    return Utf8OrdinalComparer.Instance.Compare(left.BindingId ?? string.Empty, right.BindingId ?? string.Empty);
                });
                for (var index = 0; index < selected.Count; index++) yield return selected[index];
            }
        }

        private static byte[] BuildConfigBlob(CompiledProgram program, IList<GeneratedNodeDescriptor> descriptors,
            GeneratedScopeCompilationResult scopes, IList<GeneratedCompiledAccessRecordV2> accesses)
        {
            var result = new List<byte>(program.ConfigBlob).ToArray();
            var descriptorMap = new Dictionary<string, GeneratedNodeDescriptor>(StringComparer.Ordinal);
            for (var index = 0; index < descriptors.Count; index++) descriptorMap.Add(NumericIdentity(descriptors[index]), descriptors[index]);
            for (var nodeIndex = 0; nodeIndex < program.Nodes.Count; nodeIndex++)
            {
                var nodeId = program.DebugMap[nodeIndex].AuthoringNodeId;
                if (!scopes.Configurations.TryGetValue(nodeId, out var packed)) continue;
                var record = program.Nodes[nodeIndex];
                if (packed.Length != record.ConfigSize) throw new InvalidOperationException("Generated config size differs from the semantic node record.");
                Buffer.BlockCopy(packed, 0, result, (int)record.ConfigOffset, packed.Length);
                if (!descriptorMap.TryGetValue(NumericIdentity(record.NodeTypeId, record.NodeTypeVersion), out var descriptor))
                    throw new InvalidOperationException("Generated node record has no descriptor.");
                for (var fieldIndex = 0; fieldIndex < descriptor.Configuration.Count; fieldIndex++)
                {
                    var field = descriptor.Configuration[fieldIndex];
                    if (field.BindingId == null) continue;
                    GeneratedBindingDescriptor binding = null;
                    for (var bindingIndex = 0; bindingIndex < descriptor.Bindings.Count; bindingIndex++)
                        if (descriptor.Bindings[bindingIndex].BindingId == field.BindingId)
                        {
                            binding = descriptor.Bindings[bindingIndex];
                            break;
                        }
                    if (binding == null)
                        throw new InvalidOperationException("Generated configuration handle has no binding authority.");
                    var fieldOffset = checked(record.ConfigOffset + field.Offset);
                    if (!binding.IsBlackboard)
                    {
                        if (ReadU32(result, fieldOffset) != binding.Ordinal)
                            throw new InvalidOperationException("Generated non-blackboard handle differs from its local binding ordinal.");
                        continue;
                    }
                    GeneratedCompiledAccessRecordV2 match = null;
                    for (var accessIndex = 0; accessIndex < accesses.Count; accessIndex++)
                        if (accesses[accessIndex].NodeIndex == nodeIndex && accesses[accessIndex].BindingId == field.BindingId) { match = accesses[accessIndex]; break; }
                    if (match == null) throw new InvalidOperationException("Generated binding has no final access ordinal.");
                    WriteU32(result, fieldOffset, match.AccessOrdinal);
                }
            }
            return result;
        }

        private static List<GeneratedCompiledSlotRecordV2> BuildSlots(IReadOnlyList<GeneratedScopeSlot> slots,
            IList<GeneratedCompiledAccessRecordV2> accesses, IList<GeneratedWatchedSlotV2> watched, out byte[] defaults)
        {
            var bytes = new List<byte>();
            var records = new List<GeneratedCompiledSlotRecordV2>();
            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                var offset = checked((uint)bytes.Count);
                bytes.AddRange(slot.DefaultBytes ?? Array.Empty<byte>());
                var flags = CompiledBlackboardAccessFlags.None;
                for (var accessIndex = 0; accessIndex < accesses.Count; accessIndex++)
                    if (accesses[accessIndex].Scope == slot.Key.Scope && accesses[accessIndex].SlotIndex == slot.SlotIndex)
                    {
                        if (accesses[accessIndex].Mode != GeneratedAccessModeV2.Write) flags |= CompiledBlackboardAccessFlags.Read;
                        if (accesses[accessIndex].Mode != GeneratedAccessModeV2.Read) flags |= CompiledBlackboardAccessFlags.Write;
                    }
                for (var watchedIndex = 0; watchedIndex < watched.Count; watchedIndex++)
                    if (watched[watchedIndex].Scope == slot.Key.Scope && watched[watchedIndex].SlotIndex == slot.SlotIndex)
                        flags |= CompiledBlackboardAccessFlags.Observed;
                records.Add(new GeneratedCompiledSlotRecordV2(slot, offset, flags));
            }
            defaults = bytes.ToArray();
            return records;
        }

        private static List<GeneratedWatchedSlotV2> BuildWatched(CompiledProgram program, IReadOnlyList<GeneratedScopeSlot> slots)
        {
            var result = new List<GeneratedWatchedSlotV2>();
            var canonicalSlots = new List<GeneratedScopeSlot>(slots);
            canonicalSlots.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Key.Id, right.Key.Id));
            for (var index = 0; index < program.WatchedSlotIndices.Count; index++)
            {
                var semanticSlot = program.WatchedSlotIndices[index];
                if (semanticSlot >= canonicalSlots.Count) throw new InvalidOperationException("Observed semantic slot is absent from the v2 scope table.");
                var match = canonicalSlots[(int)semanticSlot];
                result.Add(new GeneratedWatchedSlotV2(match.Key.Scope, match.SlotIndex));
            }
            return result;
        }

        private static TreeDocument CreateSemanticProjection(TreeDocument document, IList<GeneratedNodeDescriptor> descriptors)
        {
            var generated = new Dictionary<string, NodeBehaviorKind>(StringComparer.Ordinal);
            for (var index = 0; index < descriptors.Count; index++) generated.Add(Identity(descriptors[index].Manifest.TypeId, descriptors[index].Manifest.Version), descriptors[index].Manifest.Kind);
            var keys = new List<BlackboardKeyDefinition>(document.Blackboard.Count);
            for (var index = 0; index < document.Blackboard.Count; index++)
            {
                var key = document.Blackboard[index];
                keys.Add(new BlackboardKeyDefinition(key.Id, key.Name, BlackboardTypeReference.BuiltIn(BlackboardValueType.Int32), BlackboardScope.Tree,
                    BlackboardDefaultValue.Int32(0), key.Description, BlackboardReductionKind.None));
            }
            var nodes = new List<NodeDocument>(document.Nodes.Count);
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                var node = document.Nodes[index];
                var isGenerated = generated.TryGetValue(Identity(node.TypeId, (uint)node.TypeVersion), out var kind);
                var projectionType = kind == NodeBehaviorKind.Decorator
                    ? BuiltInNodeManifests.InverterTypeId
                    : BuiltInNodeManifests.MemorySequenceTypeId;
                nodes.Add(new NodeDocument(node.Id,
                    isGenerated ? projectionType : node.TypeId,
                    isGenerated ? 1 : node.TypeVersion,
                    node.Children,
                    isGenerated ? new SemanticObject(Array.Empty<SemanticProperty>()) : node.Parameters,
                    null, node.DisplayName, node.Description, node.Tags));
            }
            return new TreeDocument(TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion, document.TreeId,
                document.Name, document.Root, nodes, keys, document.Description, document.Tags, document.Metadata, document.Revision);
        }

        private static CompiledProgram BuildFinalSemanticProgram(TreeDocument document, CompiledProgram template,
            NodeRegistry registry, IList<GeneratedNodeDescriptor> descriptors, GeneratedScopeCompilationResult scopes,
            ReferenceCompilerOptions options, RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var descriptorMap = new Dictionary<string, GeneratedNodeDescriptor>(StringComparer.Ordinal);
            for (var index = 0; index < descriptors.Count; index++)
                descriptorMap.Add(Identity(descriptors[index].Manifest.TypeId, descriptors[index].Manifest.Version), descriptors[index]);
            var documentNodes = new Dictionary<NodeId, NodeDocument>();
            for (var index = 0; index < document.Nodes.Count; index++) documentNodes.Add(document.Nodes[index].Id, document.Nodes[index]);

            var nodes = new List<CompiledNodeRecord>(template.Nodes.Count);
            var configBlob = new List<byte>();
            ulong memoryCursor = 0;
            uint maximumAlignment = 1;
            for (var nodeIndex = 0; nodeIndex < template.Nodes.Count; nodeIndex++)
            {
                var templateNode = template.Nodes[nodeIndex];
                var node = documentNodes[template.DebugMap[nodeIndex].AuthoringNodeId];
                if (!registry.TryGet(node.TypeId, out var registryEntry) || registryEntry.Manifest.Version != (uint)node.TypeVersion)
                    throw new InvalidOperationException("Final node identity is absent from the combined registry.");
                var manifest = registryEntry.Manifest;
                var generated = descriptorMap.TryGetValue(Identity(node.TypeId, (uint)node.TypeVersion), out var descriptor);
                var configSize = generated ? descriptor.Manifest.Configuration.Size : templateNode.ConfigSize;
                var configAlignment = generated ? descriptor.Manifest.Configuration.Alignment : templateNode.ConfigAlignment;
                var configOffset = configSize == 0 ? 0u : checked((uint)Align((ulong)configBlob.Count, configAlignment));
                while (configBlob.Count < configOffset) configBlob.Add(0);
                if (configSize != 0)
                {
                    byte[] source;
                    if (generated)
                    {
                        if (!scopes.Configurations.TryGetValue(node.Id, out source) || source.Length != configSize)
                            throw new InvalidOperationException("Generated configuration does not match its final node record.");
                    }
                    else
                    {
                        source = new byte[templateNode.ConfigSize];
                        for (var byteIndex = 0; byteIndex < source.Length; byteIndex++)
                            source[byteIndex] = template.ConfigBlob[(int)templateNode.ConfigOffset + byteIndex];
                    }
                    configBlob.AddRange(source);
                }

                var memorySize = generated ? descriptor.Manifest.Memory.Size : templateNode.InstanceMemorySize;
                var memoryAlignment = generated ? descriptor.Manifest.Memory.Alignment : templateNode.InstanceMemoryAlignment;
                var memoryOffset = memorySize == 0 ? 0u : checked((uint)Align(memoryCursor, memoryAlignment));
                if (memorySize != 0) memoryCursor = checked((ulong)memoryOffset + memorySize);
                if (memoryAlignment > maximumAlignment) maximumAlignment = memoryAlignment;
                nodes.Add(new CompiledNodeRecord(registryEntry.NumericTypeId, manifest.Version,
                    configOffset, configSize, configAlignment, memoryOffset, memorySize, memoryAlignment,
                    manifest.Memory.Lifetime, templateNode.Children, Flags(manifest), templateNode.DebugIdentityIndex,
                    templateNode.ReadSlots, templateNode.WriteSlots));
            }
            memoryCursor = Align(memoryCursor, maximumAlignment);
            var canonical = CanonicalTreeJson.Serialize(document, registeredTypes);
            if (!canonical.Success) throw new InvalidOperationException("Version-2 semantic source is not canonically serializable.");
            BuildProjectedObserverTables(document, template.DebugMap, registry, out var observers, out var watchedSlots);
            var observed = new HashSet<uint>(watchedSlots);
            var canonicalScopeSlots = new List<GeneratedScopeSlot>(scopes.Slots);
            canonicalScopeSlots.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Key.Id, right.Key.Id));
            var semanticSlots = new List<CompiledBlackboardSlotRecord>(canonicalScopeSlots.Count);
            var semanticDefaults = new List<byte>();
            for (var slotIndex = 0; slotIndex < canonicalScopeSlots.Count; slotIndex++)
            {
                var source = canonicalScopeSlots[slotIndex];
                var layout = source.Key.Type.RuntimeDescriptor;
                var flags = template.BlackboardSlots[slotIndex].AccessFlags | (observed.Contains((uint)slotIndex) ? CompiledBlackboardAccessFlags.Observed : 0);
                var defaultOffset = (uint)semanticDefaults.Count;
                semanticDefaults.AddRange(source.DefaultBytes ?? new byte[layout.Size]);
                semanticSlots.Add(new CompiledBlackboardSlotRecord(StableHash.Fnv1A64(source.Key.Id), layout.TypeId, layout.Version,
                    source.Key.Type.EnumContractId, BlackboardScope.Tree, source.Offset, (uint)layout.Size, (uint)layout.Alignment, defaultOffset, flags));
            }
            var header = template.Header;
            var capabilities = header.CapabilityFlags | (uint)registry.Capabilities;
            for (var descriptorIndex = 0; descriptorIndex < scopes.Descriptors.Count; descriptorIndex++)
                capabilities |= scopes.Descriptors[descriptorIndex].Scope == BlackboardScope.Agent
                    ? GeneratedCompiledProgramV2.AgentScopeCapability
                    : GeneratedCompiledProgramV2.SharedScopeCapability;
            var finalHeader = new CompiledProgramHeader(2, header.ExecutionSemanticsVersion, header.CompilerVersion,
                new CompiledHash(StableHash.Sha256Hex(canonical.Utf8)), new CompiledHash(registry.Hash),
                options.Policy.ComputeHash(), header.PolicyFormatVersion, header.CompiledContentHash,
                header.RootNodeIndex, (uint)nodes.Count, (uint)template.ChildIndices.Count,
                (uint)template.BlackboardSlots.Count, (uint)template.DebugMap.Count, (uint)configBlob.Count,
                checked((uint)memoryCursor), maximumAlignment, capabilities,
                header.DeterministicModeCompatible);
            return new CompiledProgram(finalHeader, nodes, template.ChildIndices, template.ReadSlotIndices,
                template.WriteSlotIndices, semanticSlots, observers, watchedSlots,
                configBlob, semanticDefaults, template.DebugMap);
        }

        private static void BuildProjectedObserverTables(TreeDocument document, IReadOnlyList<CompiledDebugMapEntry> debugMap,
            NodeRegistry registry, out List<CompiledObserverRecord> observers, out List<uint> watchedSlots)
        {
            observers = new List<CompiledObserverRecord>();
            watchedSlots = new List<uint>();
            var nodes = new Dictionary<NodeId, NodeDocument>();
            var indices = new Dictionary<NodeId, uint>();
            var parents = new Dictionary<NodeId, NodeDocument>();
            for (var index = 0; index < document.Nodes.Count; index++) nodes.Add(document.Nodes[index].Id, document.Nodes[index]);
            for (var index = 0; index < debugMap.Count; index++) indices.Add(debugMap[index].AuthoringNodeId, (uint)index);
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                var parent = document.Nodes[index];
                for (var childIndex = 0; childIndex < parent.Children.Count; childIndex++)
                {
                    if (parents.ContainsKey(parent.Children[childIndex]))
                        throw new InvalidOperationException("Observer topology requires each node to have at most one parent.");
                    parents.Add(parent.Children[childIndex], parent);
                }
            }
            var canonicalKeys = new List<BlackboardKeyDefinition>(document.Blackboard);
            canonicalKeys.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id, right.Id));
            var slotById = new Dictionary<string, uint>(StringComparer.Ordinal);
            for (var index = 0; index < canonicalKeys.Count; index++) slotById.Add(canonicalKeys[index].Id, (uint)index);
            for (var runtimeIndex = 0; runtimeIndex < debugMap.Count; runtimeIndex++)
            {
                var node = nodes[debugMap[runtimeIndex].AuthoringNodeId];
                if (node.Observer == null) continue;
                if (!registry.TryGet(node.TypeId, out var entry) || entry.Manifest.Version != (uint)node.TypeVersion
                    || entry.Manifest.Kind != NodeBehaviorKind.Condition)
                    throw new InvalidOperationException("Only an exact registered Condition node may own a version-2 observer.");
                if (!parents.TryGetValue(node.Id, out var parent)
                    || (parent.TypeId != BuiltInNodeManifests.ReactiveSequenceTypeId && parent.TypeId != BuiltInNodeManifests.ReactiveSelectorTypeId))
                    throw new InvalidOperationException("A version-2 observer must be a direct child of one reactive composite.");
                var mode = ObserverMode(node.Observer.Mode);
                if (mode != CompiledObserverMode.Self && parent.TypeId != BuiltInNodeManifests.ReactiveSelectorTypeId)
                    throw new InvalidOperationException("Lower-priority observer modes require a reactive selector.");
                var watchedIds = new List<string>(node.Observer.WatchedKeys);
                watchedIds.Sort((left, right) =>
                {
                    var comparison = StableHash.Fnv1A64(left).CompareTo(StableHash.Fnv1A64(right));
                    return comparison != 0 ? comparison : Utf8OrdinalComparer.Instance.Compare(left, right);
                });
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var range = new CompiledRange((uint)watchedSlots.Count, (uint)watchedIds.Count);
                for (var watchedIndex = 0; watchedIndex < watchedIds.Count; watchedIndex++)
                {
                    if (!seen.Add(watchedIds[watchedIndex]) || !slotById.TryGetValue(watchedIds[watchedIndex], out var slotIndex))
                        throw new InvalidOperationException("Observer watched keys must be declared and unique.");
                    watchedSlots.Add(slotIndex);
                }
                if (range.IsEmpty) throw new InvalidOperationException("Observers require at least one watched key.");
                observers.Add(new CompiledObserverRecord((uint)runtimeIndex, indices[parent.Id], mode, range));
            }
        }

        private static CompiledObserverMode ObserverMode(string value)
        {
            if (value == "self") return CompiledObserverMode.Self;
            if (value == "lower-priority") return CompiledObserverMode.LowerPriority;
            if (value == "both") return CompiledObserverMode.Both;
            throw new InvalidOperationException("Observer mode is outside the closed version-2 contract.");
        }

        private static CompiledNodeFlags Flags(NodeManifest manifest)
        {
            return manifest.ExecutionDomain == NodeExecutionDomain.Burst ? CompiledNodeFlags.BurstDomain
                : manifest.ExecutionDomain == NodeExecutionDomain.Managed ? CompiledNodeFlags.ManagedDomain : CompiledNodeFlags.MainThreadDomain;
        }

        private static ulong Align(ulong value, uint alignment)
        {
            var mask = (ulong)alignment - 1;
            return checked((value + mask) & ~mask);
        }

        private static Dictionary<string, GeneratedScopeSlot> IndexSlots(IReadOnlyList<GeneratedScopeSlot> slots)
        { var result = new Dictionary<string, GeneratedScopeSlot>(StringComparer.Ordinal); for (var i = 0; i < slots.Count; i++) result.Add(slots[i].Key.Id, slots[i]); return result; }
        private static string Identity(string typeId, uint version) => typeId + "\0" + version.ToString(CultureInfo.InvariantCulture);
        private static string NumericIdentity(GeneratedNodeDescriptor descriptor) => NumericIdentity(StableHash.Fnv1A64(descriptor.Manifest.TypeId), descriptor.Manifest.Version);
        private static string NumericIdentity(ulong typeId, uint version) => typeId.ToString("x16", CultureInfo.InvariantCulture) + "\0" + version.ToString(CultureInfo.InvariantCulture);
        private static void WriteU32(byte[] bytes, uint offset, uint value) { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); bytes[offset + 2] = (byte)(value >> 16); bytes[offset + 3] = (byte)(value >> 24); }
        private static uint ReadU32(byte[] bytes, uint offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        private static void Add(ICollection<Diagnostic> target, DiagnosticCollection source) { for (var i = 0; i < source.Count; i++) target.Add(source[i]); }
        private static bool HasError(IReadOnlyList<Diagnostic> values) { for (var i = 0; i < values.Count; i++) if (values[i].Severity == DiagnosticSeverity.Error) return true; return false; }
        private static GeneratedCompiledProgramV2Result Failure(IList<Diagnostic> diagnostics) => new GeneratedCompiledProgramV2Result(null, new DiagnosticCollection(diagnostics));
    }

    internal static class GeneratedCompiledProgramV2Serializer
    {
        internal static byte[] Serialize(GeneratedCompiledProgramV2 value, byte[] configBlob, byte[] defaultBlob)
        {
            var program = value.SemanticProgram; var header = program.Header; var writer = new GeneratedByteWriter(null);
            writer.U32(header.Magic); writer.U32(2); writer.U32(header.ExecutionSemanticsVersion);
            writer.U16(header.CompilerVersion.Major); writer.U16(header.CompilerVersion.Minor); writer.U16(header.CompilerVersion.Patch); writer.U32(header.CompilerVersion.BuildRevision);
            writer.Hash(header.CanonicalSemanticHash.HexadecimalValue); writer.Hash(header.NodeRegistryHash.HexadecimalValue); writer.Hash(header.CanonicalPolicyHash.HexadecimalValue);
            writer.U32(header.PolicyFormatVersion); writer.U32(header.RootNodeIndex); writer.U32((uint)program.Nodes.Count); writer.U32((uint)program.ChildIndices.Count);
            writer.U32((uint)value.Slots.Count); writer.U32((uint)program.DebugMap.Count); writer.U32((uint)configBlob.Length); writer.U32(header.InstanceNodeMemorySize);
            writer.U32(header.RequiredMaximumAlignment); var capabilities = header.CapabilityFlags; for (var i = 0; i < value.Scopes.Descriptors.Count; i++) capabilities |= value.Scopes.Descriptors[i].Scope == BlackboardScope.Agent ? GeneratedCompiledProgramV2.AgentScopeCapability : GeneratedCompiledProgramV2.SharedScopeCapability;
            writer.U32(capabilities); writer.U8(header.DeterministicModeCompatible ? (byte)1 : (byte)0);
            writer.U32((uint)value.Scopes.Descriptors.Count);
            for (var i = 0; i < value.Scopes.Descriptors.Count; i++)
            { var item = value.Scopes.Descriptors[i]; writer.U8(Scope(item.Scope)); writer.String(item.Contract.ContractId); writer.U64(StableHash.Fnv1A64(item.Contract.ContractId)); writer.U32(item.Contract.ContractVersion); writer.Hash(item.SchemaHash.HexadecimalValue); writer.Hash(item.LayoutHash.HexadecimalValue); writer.U32(item.FirstSlot); writer.U32(item.SlotCount); }
            for (var i = 0; i < program.Nodes.Count; i++)
            {
                var node = program.Nodes[i]; writer.U64(node.NodeTypeId); writer.U32(node.NodeTypeVersion); writer.U32(node.ConfigOffset); writer.U32(node.ConfigSize); writer.U32(node.ConfigAlignment);
                writer.U32(node.InstanceMemoryOffset); writer.U32(node.InstanceMemorySize); writer.U32(node.InstanceMemoryAlignment); writer.U8((byte)node.MemoryLifetime);
                writer.U32(node.Children.Offset); writer.U32(node.Children.Count); writer.U32((uint)node.Flags); writer.U32(node.DebugIdentityIndex);
                uint firstRead = 0, readCount = 0, firstWrite = 0, writeCount = 0; var seenRead = false; var seenWrite = false;
                for (var a = 0; a < value.Accesses.Count; a++) if (value.Accesses[a].NodeIndex == i) { if (value.Accesses[a].Mode != GeneratedAccessModeV2.Write) { if (!seenRead) { firstRead = (uint)a; seenRead = true; } readCount++; } if (value.Accesses[a].Mode != GeneratedAccessModeV2.Read) { if (!seenWrite) { firstWrite = (uint)a; seenWrite = true; } writeCount++; } }
                writer.U32(firstRead); writer.U32(readCount); writer.U32(firstWrite); writer.U32(writeCount);
            }
            for (var i = 0; i < program.ChildIndices.Count; i++) writer.U32(program.ChildIndices[i]);
            writer.U32((uint)value.Accesses.Count);
            for (var i = 0; i < value.Accesses.Count; i++) { var item = value.Accesses[i]; writer.U32(item.NodeIndex); writer.U32(item.AccessOrdinal); writer.U8(Scope(item.Scope)); writer.U32(item.SlotIndex); writer.U8((byte)item.Mode); writer.U8((byte)item.Reduction); }
            writer.U32((uint)value.Slots.Count);
            for (var i = 0; i < value.Slots.Count; i++)
            { var item = value.Slots[i]; var slot = item.Slot; var layout = slot.Key.Type.RuntimeDescriptor; writer.String(slot.Key.Id); writer.U64(StableHash.Fnv1A64(slot.Key.Id)); writer.U64(layout.TypeId); writer.U32(layout.Version); writer.U64(slot.Key.Type.EnumContractId); writer.U8(Scope(slot.Key.Scope)); writer.U32(slot.SlotIndex); writer.U32(slot.Offset); writer.U32((uint)layout.Size); writer.U32((uint)layout.Alignment); writer.U32(item.DefaultOffset); writer.U32((uint)(slot.DefaultBytes?.Length ?? 0)); writer.U8((byte)item.AccessFlags); writer.U32(uint.MaxValue); writer.U32(0); }
            writer.U32((uint)program.Observers.Count); for (var i = 0; i < program.Observers.Count; i++) { var item = program.Observers[i]; writer.U32(item.ObserverNodeIndex); writer.U32(item.OwningReactiveCompositeIndex); writer.U8((byte)item.Mode); writer.U32(item.WatchedSlots.Offset); writer.U32(item.WatchedSlots.Count); }
            writer.U32((uint)value.WatchedSlots.Count); for (var i = 0; i < value.WatchedSlots.Count; i++) { writer.U8(Scope(value.WatchedSlots[i].Scope)); writer.U32(value.WatchedSlots[i].SlotIndex); }
            writer.Bytes(configBlob); writer.Bytes(defaultBlob); writer.U32((uint)value.Scopes.Descriptors.Count); for (var i = 0; i < value.Scopes.Descriptors.Count; i++) writer.Bytes(value.Scopes.Descriptors[i].GetRawLayoutCopy());
            writer.U32((uint)program.DebugMap.Count); for (var i = 0; i < program.DebugMap.Count; i++) { var item = program.DebugMap[i]; writer.U32(item.RuntimeNodeIndex); writer.String(item.AuthoringNodeId.Value); writer.String(item.SourcePath); writer.String(item.DisplayName ?? string.Empty); }
            return writer.ToArray();
        }
        private static byte Scope(BlackboardScope value) => value == BlackboardScope.Tree ? (byte)0 : value == BlackboardScope.Agent ? (byte)1 : (byte)2;
    }
}
