using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AIBT.Authoring
{
    public sealed class GeneratedScopeDescriptor
    {
        private readonly byte[] _schema;
        private readonly byte[] _rawLayout;

        internal GeneratedScopeDescriptor(BlackboardScope scope, BlackboardScopeContract contract, CompiledHash schemaHash, CompiledHash layoutHash, uint firstSlot, uint slotCount, byte[] schema, byte[] rawLayout)
        {
            Scope = scope; Contract = contract; SchemaHash = schemaHash; LayoutHash = layoutHash; FirstSlot = firstSlot; SlotCount = slotCount;
            _schema = (byte[])(schema ?? throw new ArgumentNullException(nameof(schema))).Clone();
            _rawLayout = (byte[])(rawLayout ?? throw new ArgumentNullException(nameof(rawLayout))).Clone();
        }
        public BlackboardScope Scope { get; }
        public BlackboardScopeContract Contract { get; }
        public CompiledHash SchemaHash { get; }
        public CompiledHash LayoutHash { get; }
        public uint FirstSlot { get; }
        public uint SlotCount { get; }
        public byte[] GetSchemaBytesCopy() => (byte[])_schema.Clone();
        public byte[] GetRawLayoutCopy() => (byte[])_rawLayout.Clone();
    }

    public sealed class GeneratedScopeSlot
    {
        internal GeneratedScopeSlot(BlackboardKeyDefinition key, uint slotIndex, uint offset, byte[] defaultBytes)
        {
            Key = key; SlotIndex = slotIndex; Offset = offset; DefaultBytes = defaultBytes;
        }
        public BlackboardKeyDefinition Key { get; }
        public uint SlotIndex { get; }
        public uint Offset { get; }
        public byte[] DefaultBytes { get; }
    }

    public sealed class GeneratedNodeAccessRecord
    {
        internal GeneratedNodeAccessRecord(NodeId nodeId, GeneratedBindingDescriptor binding, uint scopeSlot, BlackboardReductionKind reduction)
        {
            NodeId = nodeId; Binding = binding; ScopeSlot = scopeSlot; Reduction = reduction;
        }
        public NodeId NodeId { get; }
        public GeneratedBindingDescriptor Binding { get; }
        public uint AccessOrdinal => Binding.Ordinal;
        public uint ScopeSlot { get; }
        public BlackboardScope Scope => Binding.Scope;
        public BlackboardReductionKind Reduction { get; }
    }

    public sealed class GeneratedScopeCompilationResult
    {
        internal GeneratedScopeCompilationResult(IList<GeneratedScopeDescriptor> descriptors, IList<GeneratedScopeSlot> slots, IList<GeneratedNodeAccessRecord> accesses, IDictionary<NodeId, byte[]> configurations, DiagnosticCollection diagnostics)
        {
            Descriptors = new List<GeneratedScopeDescriptor>(descriptors).AsReadOnly();
            Slots = new List<GeneratedScopeSlot>(slots).AsReadOnly();
            Accesses = new List<GeneratedNodeAccessRecord>(accesses).AsReadOnly();
            Configurations = new ReadOnlyDictionary<NodeId, byte[]>(new Dictionary<NodeId, byte[]>(configurations));
            Diagnostics = diagnostics;
        }
        public IReadOnlyList<GeneratedScopeDescriptor> Descriptors { get; }
        public IReadOnlyList<GeneratedScopeSlot> Slots { get; }
        public IReadOnlyList<GeneratedNodeAccessRecord> Accesses { get; }
        public IReadOnlyDictionary<NodeId, byte[]> Configurations { get; }
        public DiagnosticCollection Diagnostics { get; }
        public bool Success => Diagnostics.Count == 0;
    }

    public sealed class GeneratedScopeCompilationInput
    {
        private readonly ReadOnlyCollection<GeneratedNodeDescriptor> _generatedNodes;

        public GeneratedScopeCompilationInput(TreeDocument document, IEnumerable<GeneratedNodeDescriptor> generatedNodes, string documentId = null)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            _generatedNodes = new List<GeneratedNodeDescriptor>(generatedNodes ?? throw new ArgumentNullException(nameof(generatedNodes))).AsReadOnly();
            DocumentId = documentId;
        }

        public TreeDocument Document { get; }
        public IReadOnlyList<GeneratedNodeDescriptor> GeneratedNodes => _generatedNodes;
        public string DocumentId { get; }
    }

    public sealed class GeneratedScopeCompilationSetResult
    {
        private readonly ReadOnlyCollection<GeneratedScopeCompilationResult> _results;

        internal GeneratedScopeCompilationSetResult(IList<GeneratedScopeCompilationResult> results, DiagnosticCollection diagnostics)
        {
            _results = new List<GeneratedScopeCompilationResult>(results).AsReadOnly();
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<GeneratedScopeCompilationResult> Results => _results;
        public DiagnosticCollection Diagnostics { get; }
        public bool Success => Diagnostics.Count == 0;
    }

    public static class GeneratedScopeCompiler
    {
        public static GeneratedScopeCompilationSetResult CompileSet(IEnumerable<GeneratedScopeCompilationInput> inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            var ordered = new List<GeneratedScopeCompilationInput>(inputs);
            ordered.Sort((left, right) =>
            {
                var comparison = Utf8OrdinalComparer.Instance.Compare(left.Document.TreeId.Value, right.Document.TreeId.Value);
                return comparison != 0 ? comparison : Utf8OrdinalComparer.Instance.Compare(left.DocumentId ?? string.Empty, right.DocumentId ?? string.Empty);
            });
            var results = new List<GeneratedScopeCompilationResult>(ordered.Count);
            var diagnostics = new List<Diagnostic>();
            var contracts = new Dictionary<string, SetContract>(StringComparer.Ordinal);
            for (var inputIndex = 0; inputIndex < ordered.Count; inputIndex++)
            {
                var input = ordered[inputIndex];
                var result = Compile(input.Document, input.GeneratedNodes, input.DocumentId);
                results.Add(result);
                for (var diagnosticIndex = 0; diagnosticIndex < result.Diagnostics.Count; diagnosticIndex++)
                    diagnostics.Add(result.Diagnostics[diagnosticIndex]);
                for (var descriptorIndex = 0; descriptorIndex < result.Descriptors.Count; descriptorIndex++)
                {
                    var descriptor = result.Descriptors[descriptorIndex];
                    var identity = descriptor.Contract.ContractId + "\0" + descriptor.Contract.ContractVersion.ToString(CultureInfo.InvariantCulture);
                    var pointer = "/blackboardContracts/" + ScopeText(descriptor.Scope);
                    if (!contracts.TryGetValue(identity, out var existing))
                    {
                        contracts.Add(identity, new SetContract(descriptor, input.DocumentId, input.Document.TreeId, pointer));
                        continue;
                    }
                    if (descriptor.SchemaHash == existing.Descriptor.SchemaHash && descriptor.LayoutHash == existing.Descriptor.LayoutHash)
                        continue;
                    diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                        ReferenceCompilerDiagnosticCodes.ScopeContractMismatch,
                        "Scope contract identity/version resolves to different canonical schema or layout metadata across the compilation set.",
                        input.DocumentId, pointer, input.Document.TreeId,
                        relatedLocations: new[] { new DiagnosticLocation(existing.DocumentId, existing.JsonPointer, treeId: existing.TreeId) }));
                }
            }
            return new GeneratedScopeCompilationSetResult(results, new DiagnosticCollection(diagnostics));
        }

        public static GeneratedScopeCompilationResult Compile(TreeDocument document, IEnumerable<GeneratedNodeDescriptor> generatedNodes, string documentId = null)
            => Compile(document, generatedNodes, null, documentId);

        public static GeneratedScopeCompilationResult Compile(TreeDocument document, IEnumerable<GeneratedNodeDescriptor> generatedNodes,
            RegisteredBlackboardTypeCatalog registeredTypes, string documentId = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (generatedNodes == null) throw new ArgumentNullException(nameof(generatedNodes));
            var diagnostics = new List<Diagnostic>();
            var descriptorsByType = new Dictionary<string, GeneratedNodeDescriptor>(StringComparer.Ordinal);
            foreach (var descriptor in generatedNodes)
            {
                if (descriptor == null) throw new ArgumentException("Generated descriptors cannot contain null.", nameof(generatedNodes));
                var identity = descriptor.Manifest.TypeId + "\0" + descriptor.Manifest.Version.ToString(CultureInfo.InvariantCulture);
                if (descriptorsByType.ContainsKey(identity))
                    Add(diagnostics, ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding, "Duplicate generated node descriptor.", documentId, null, default);
                else descriptorsByType.Add(identity, descriptor);
            }

            var hasExtended = false;
            for (var index = 0; index < document.Blackboard.Count; index++)
                hasExtended |= document.Blackboard[index].Scope == BlackboardScope.Agent || document.Blackboard[index].Scope == BlackboardScope.Shared;
            for (var index = 0; index < document.Nodes.Count; index++) hasExtended |= document.Nodes[index].Bindings != null;
            if (hasExtended && document.FormatVersion != 2)
                Add(diagnostics, ReferenceCompilerDiagnosticCodes.MissingScopeContract, "Generated bindings and Agent/Shared scopes require tree format version 2.", documentId, "/formatVersion", default);

            var slots = BuildSlots(document, registeredTypes, diagnostics, documentId);
            var scopeDescriptors = BuildScopeDescriptors(document, slots, diagnostics, documentId);
            var accesses = new List<GeneratedNodeAccessRecord>();
            var configurations = new Dictionary<NodeId, byte[]>();
            var keyById = new Dictionary<string, GeneratedScopeSlot>(StringComparer.Ordinal);
            for (var index = 0; index < slots.Count; index++)
                if (!keyById.ContainsKey(slots[index].Key.Id)) keyById.Add(slots[index].Key.Id, slots[index]);

            for (var index = 0; index < document.Nodes.Count; index++)
            {
                var node = document.Nodes[index];
                var identity = node.TypeId + "\0" + ((uint)node.TypeVersion).ToString(CultureInfo.InvariantCulture);
                if (!descriptorsByType.TryGetValue(identity, out var descriptor)) continue;
                var ordinals = CompileBindings(node, descriptor, keyById, accesses, diagnostics, documentId);
                var packed = GeneratedConfigurationPacker.Pack(descriptor, node.Parameters, ordinals, documentId, node.Id);
                for (var diagnosticIndex = 0; diagnosticIndex < packed.Diagnostics.Count; diagnosticIndex++) diagnostics.Add(packed.Diagnostics[diagnosticIndex]);
                if (packed.Success) configurations[node.Id] = packed.Bytes;
            }

            ValidateLiteralSharedWrites(document, descriptorsByType, keyById, diagnostics, documentId);
            return new GeneratedScopeCompilationResult(scopeDescriptors, slots, accesses, configurations, new DiagnosticCollection(diagnostics));
        }

        private static List<GeneratedScopeSlot> BuildSlots(TreeDocument document, RegisteredBlackboardTypeCatalog registeredTypes, List<Diagnostic> diagnostics, string documentId)
        {
            var slots = new List<GeneratedScopeSlot>();
            foreach (var scope in new[] { BlackboardScope.Tree, BlackboardScope.Agent, BlackboardScope.Shared })
            {
                var keys = new List<BlackboardKeyDefinition>();
                for (var index = 0; index < document.Blackboard.Count; index++) if (document.Blackboard[index].Scope == scope) keys.Add(document.Blackboard[index]);
                keys.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id, right.Id));
                uint offset = 0;
                for (var index = 0; index < keys.Count; index++)
                {
                    var key = keys[index];
                    if (!Enum.IsDefined(typeof(BlackboardReductionKind), key.Reduction))
                        Add(diagnostics, ReferenceCompilerDiagnosticCodes.UnsupportedReduction, "Custom or unknown reducers are not supported by contract v1.", documentId, "/blackboard/" + Escape(key.Id) + "/reduction", default);
                    else if (!ValidReduction(key)) Add(diagnostics, ReferenceCompilerDiagnosticCodes.InvalidReduction, "Reduction is incompatible with scope or type.", documentId, "/blackboard/" + Escape(key.Id) + "/reduction", default);
                    var layout = key.Type.RuntimeDescriptor;
                    offset = Align(offset, (uint)layout.Alignment);
                    byte[] defaultBytes = null;
                    if (key.DefaultValue == null)
                    {
                        if (scope != BlackboardScope.Tree)
                            Add(diagnostics, ReferenceCompilerDiagnosticCodes.DefaultValuePacking, "Agent/Shared keys require a canonical generated-codec default.", documentId, "/blackboard/" + Escape(key.Id) + "/default", default);
                        defaultBytes = new byte[layout.Size];
                    }
                    else if (!key.DefaultValue.IsCanonical)
                        Add(diagnostics, ReferenceCompilerDiagnosticCodes.DefaultValuePacking, "The blackboard default has no accepted generated codec.", documentId, "/blackboard/" + Escape(key.Id) + "/default", default);
                    else if (key.Type.IsRegistered)
                    {
                        if (registeredTypes == null
                            || !registeredTypes.TryGet(key.Type.CanonicalTypeId, key.Type.RegisteredDescriptor.Version, out var registered)
                            || registered.Descriptor != key.Type.RegisteredDescriptor
                            || !key.DefaultValue.TryGetRegisteredValue(out var registeredValue))
                            Add(diagnostics, ReferenceCompilerDiagnosticCodes.DefaultValuePacking, "Registered default is absent from the exact accepted catalog codec.", documentId, "/blackboard/" + Escape(key.Id) + "/default", default);
                        else defaultBytes = RegisteredBlackboardDefaultCodec.Encode(registered, registeredValue, registeredTypes);
                    }
                    else if (!key.DefaultValue.TryGetRuntimeValue(out var runtimeValue))
                        Add(diagnostics, ReferenceCompilerDiagnosticCodes.DefaultValuePacking, "The blackboard default has no accepted generated codec.", documentId, "/blackboard/" + Escape(key.Id) + "/default", default);
                    else defaultBytes = CompiledBlackboardValueEncoder.Encode(runtimeValue);
                    slots.Add(new GeneratedScopeSlot(key, (uint)index, offset, defaultBytes));
                    offset = checked(offset + (uint)layout.Size);
                }
            }
            return slots;
        }

        private static List<GeneratedScopeDescriptor> BuildScopeDescriptors(TreeDocument document, IList<GeneratedScopeSlot> slots, List<Diagnostic> diagnostics, string documentId)
        {
            var result = new List<GeneratedScopeDescriptor>();
            uint first = 0;
            for (var index = 0; index < slots.Count; index++)
            {
                if (slots[index].Key.Scope == BlackboardScope.Tree)
                {
                    first++;
                }
            }
            foreach (var scope in new[] { BlackboardScope.Agent, BlackboardScope.Shared })
            {
                var contract = scope == BlackboardScope.Agent ? document.AgentContract : document.SharedContract;
                var scoped = new List<GeneratedScopeSlot>();
                for (var index = 0; index < slots.Count; index++) if (slots[index].Key.Scope == scope) scoped.Add(slots[index]);
                if ((scoped.Count == 0) != (contract == null))
                {
                    Add(diagnostics, ReferenceCompilerDiagnosticCodes.MissingScopeContract, "Scope contract presence must exactly match its keys.", documentId, "/blackboardContracts/" + ScopeText(scope), default);
                    if (contract == null) continue;
                }
                if (scoped.Count == 0) continue;
                var schema = GeneratedBlackboardContractBytesV2.Schema(scope, contract, scoped);
                var schemaHash = new CompiledHash(StableHash.Sha256Hex(schema));
                var rawLayout = GeneratedBlackboardContractBytesV2.Layout(scope, contract, schemaHash, scoped);
                var layoutHash = new CompiledHash(StableHash.Sha256Hex(rawLayout));
                result.Add(new GeneratedScopeDescriptor(scope, contract, schemaHash, layoutHash, first, (uint)scoped.Count, schema, rawLayout));
                first += (uint)scoped.Count;
            }
            ValidateContractCoherence(result, diagnostics, documentId);
            return result;
        }

        private static void ValidateContractCoherence(IList<GeneratedScopeDescriptor> descriptors, IList<Diagnostic> diagnostics, string documentId)
        {
            for (var left = 0; left < descriptors.Count; left++)
            for (var right = left + 1; right < descriptors.Count; right++)
            {
                var a = descriptors[left]; var b = descriptors[right];
                if (a.Contract.ContractId == b.Contract.ContractId
                    && a.Contract.ContractVersion == b.Contract.ContractVersion
                    && (a.SchemaHash != b.SchemaHash || a.LayoutHash != b.LayoutHash))
                    Add(diagnostics, ReferenceCompilerDiagnosticCodes.ScopeContractMismatch,
                        "The same contract identity/version resolves to different scope schema or layout bytes.", documentId, "/blackboardContracts", default);
            }
        }

        private static Dictionary<string, uint> CompileBindings(NodeDocument node, GeneratedNodeDescriptor descriptor, IDictionary<string, GeneratedScopeSlot> keys, IList<GeneratedNodeAccessRecord> accesses, IList<Diagnostic> diagnostics, string documentId)
        {
            var result = new Dictionary<string, uint>(StringComparer.Ordinal);
            var declared = new List<GeneratedBindingDescriptor>();
            for (var index = 0; index < descriptor.Bindings.Count; index++)
            {
                var binding = descriptor.Bindings[index];
                if (binding.IsBlackboard) declared.Add(binding);
                else result.Add(binding.BindingId, binding.Ordinal);
            }
            var map = node.Bindings?.Values;
            if (declared.Count == 0)
            {
                if (map != null && map.Count != 0) AddBinding(diagnostics, node, "bindings is forbidden when the node declares no blackboard handles.", documentId);
                return result;
            }
            if (map == null || map.Count != declared.Count)
            {
                AddBinding(diagnostics, node, "bindings must contain exactly every generated blackboard binding.", documentId);
                return result;
            }
            var declaredIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < declared.Count; index++) declaredIds.Add(declared[index].BindingId);
            foreach (var pair in map) if (!declaredIds.Contains(pair.Key)) AddBinding(diagnostics, node, "Unknown generated binding '" + pair.Key + "'.", documentId);
            for (var index = 0; index < declared.Count; index++)
            {
                var binding = declared[index];
                if (!map.TryGetValue(binding.BindingId, out var keyId) || !keys.TryGetValue(keyId, out var slot))
                {
                    AddBinding(diagnostics, node, "Binding '" + binding.BindingId + "' targets a missing key.", documentId);
                    continue;
                }
                var type = binding.Types[0];
                if (slot.Key.Scope != binding.Scope || slot.Key.Type.CanonicalTypeId != type.CanonicalTypeId || slot.Key.Type.RuntimeDescriptor.Version != type.Version)
                {
                    AddBinding(diagnostics, node, "Binding '" + binding.BindingId + "' scope/type/version mismatch.", documentId);
                    continue;
                }
                result.Add(binding.BindingId, binding.Ordinal);
                accesses.Add(new GeneratedNodeAccessRecord(node.Id, binding, slot.SlotIndex, slot.Key.Reduction));
            }
            return result;
        }

        private static void ValidateLiteralSharedWrites(TreeDocument document, IDictionary<string, GeneratedNodeDescriptor> descriptors, IDictionary<string, GeneratedScopeSlot> keys, IList<Diagnostic> diagnostics, string documentId)
        {
            for (var nodeIndex = 0; nodeIndex < document.Nodes.Count; nodeIndex++)
            {
                var node = document.Nodes[nodeIndex];
                var identity = node.TypeId + "\0" + ((uint)node.TypeVersion).ToString(CultureInfo.InvariantCulture);
                if (!descriptors.TryGetValue(identity, out var descriptor)) continue;
                for (var writeIndex = 0; writeIndex < descriptor.Manifest.Writes.Count; writeIndex++)
                    if (keys.TryGetValue(descriptor.Manifest.Writes[writeIndex], out var slot) && slot.Key.Scope == BlackboardScope.Shared && slot.Key.Reduction == BlackboardReductionKind.None)
                        Add(diagnostics, ReferenceCompilerDiagnosticCodes.SharedReductionMissing, "Shared literal write requires a reducer.", documentId, "/blackboard/" + Escape(slot.Key.Id), node.Id);
            }
        }

        private static bool ValidReduction(BlackboardKeyDefinition key)
        {
            if (key.Scope != BlackboardScope.Shared) return key.Reduction == BlackboardReductionKind.None;
            if (key.Reduction == BlackboardReductionKind.None || key.Reduction == BlackboardReductionKind.First || key.Reduction == BlackboardReductionKind.Last) return true;
            if (key.Reduction == BlackboardReductionKind.Any || key.Reduction == BlackboardReductionKind.All) return key.Type.ValueType == BlackboardValueType.Bool;
            if (key.Reduction == BlackboardReductionKind.Min || key.Reduction == BlackboardReductionKind.Max || key.Reduction == BlackboardReductionKind.Sum)
                return key.Type.ValueType == BlackboardValueType.Int32 || key.Type.ValueType == BlackboardValueType.Int64 || key.Type.ValueType == BlackboardValueType.Float32 || key.Type.ValueType == BlackboardValueType.Float64;
            return false;
        }

        private static uint Align(uint value, uint alignment) => checked((value + alignment - 1) & ~(alignment - 1));
        private static string ScopeText(BlackboardScope scope) => scope == BlackboardScope.Agent ? "agent" : "shared";

        private readonly struct SetContract
        {
            internal SetContract(GeneratedScopeDescriptor descriptor, string documentId, TreeId treeId, string jsonPointer)
            { Descriptor = descriptor; DocumentId = documentId; TreeId = treeId; JsonPointer = jsonPointer; }
            internal GeneratedScopeDescriptor Descriptor { get; }
            internal string DocumentId { get; }
            internal TreeId TreeId { get; }
            internal string JsonPointer { get; }
        }
        private static string Escape(string value) => (value ?? string.Empty).Replace("~", "~0").Replace("/", "~1");
        private static void Add(IList<Diagnostic> diagnostics, DiagnosticCode code, string message, string documentId, string pointer, NodeId nodeId)
            => diagnostics.Add(ReferenceCompilerDiagnostics.Create(code, message, documentId, pointer, nodeId: nodeId));
        private static void AddBinding(IList<Diagnostic> diagnostics, NodeDocument node, string message, string documentId)
            => Add(diagnostics, ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding, message, documentId, "/nodes/" + Escape(node.Id.Value) + "/bindings", node.Id);
    }
}
