using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class ReferenceCompiler
    {
        public const uint CompiledFormatVersion = 1;
        public const uint ExecutionSemanticsVersion = 1;

        public static ReferenceCompilationResult Compile(
            TreeDocument document,
            NodeRegistry registry,
            ReferenceCompilerOptions options)
        {
            var diagnostics = new List<Diagnostic>();
            ValidateInputs(document, registry, options, diagnostics);
            if (ContainsError(diagnostics))
            {
                return Failure(diagnostics);
            }

            AddRange(diagnostics, TreeValidator.Validate(
                document,
                registry,
                options.Policy.CreateValidationOptions(options.SourceId)));
            if (ContainsError(diagnostics))
            {
                return Failure(diagnostics);
            }

            var serialization = CanonicalTreeJson.Serialize(document);
            AddRange(diagnostics, serialization.Diagnostics);
            if (!serialization.Success || ContainsError(diagnostics))
            {
                return Failure(diagnostics);
            }

            CompiledHash registryHash;
            try
            {
                registryHash = new CompiledHash(registry.Hash);
            }
            catch (ArgumentException)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure,
                    "The selected node registry has no valid canonical SHA-256 hash.",
                    options.SourceId,
                    treeId: document.TreeId));
                return Failure(diagnostics);
            }

            try
            {
                var builder = new Builder(document, registry, options, ToCompiledHash(serialization.SemanticHash), registryHash);
                var program = builder.Build();
                return new ReferenceCompilationResult(program, new DiagnosticCollection(diagnostics));
            }
            catch (CompilationFailureException exception)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    exception.Code,
                    exception.Message,
                    options.SourceId,
                    exception.JsonPointer,
                    document.TreeId,
                    exception.NodeId));
                return Failure(diagnostics);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure,
                    "The logical compiled program could not be constructed: " + exception.Message,
                    options.SourceId,
                    treeId: document.TreeId));
                return Failure(diagnostics);
            }
        }

        private static void ValidateInputs(
            TreeDocument document,
            NodeRegistry registry,
            ReferenceCompilerOptions options,
            ICollection<Diagnostic> diagnostics)
        {
            if (document == null)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "A tree document is required."));
            }

            if (registry == null)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "An explicit node registry is required."));
            }

            if (options == null)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "Reference compiler options are required."));
                return;
            }

            if (!IsCanonicalLogicalSourceId(options.SourceId))
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "SourceId must be a non-empty relative logical path using forward slashes and no '.' or '..' segments."));
            }

            if (!options.CompilerVersion.IsValid)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "A nonzero compiler version is required.",
                    SafeDocumentId(options.SourceId)));
            }

            if (options.Policy == null)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "A typed reference compilation policy is required.",
                    SafeDocumentId(options.SourceId)));
            }
            if (options.BlackboardKeyHash == null || options.TableElementLimit == 0)
            {
                diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                    ReferenceCompilerDiagnosticCodes.InvalidOptions,
                    "Compiler limits and stable-identity hashing must be configured.",
                    SafeDocumentId(options.SourceId)));
            }
        }

        private static bool IsCanonicalLogicalSourceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value[0] == '/'
                || value[value.Length - 1] == '/'
                || value.IndexOf('\\') >= 0
                || value.IndexOf(':') >= 0
                || value.IndexOf('\0') >= 0)
            {
                return false;
            }

            var segments = value.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0 || segments[index] == "." || segments[index] == "..")
                {
                    return false;
                }
            }

            return true;
        }

        private static string SafeDocumentId(string sourceId)
            => IsCanonicalLogicalSourceId(sourceId) ? sourceId : null;

        private static ReferenceCompilationResult Failure(IEnumerable<Diagnostic> diagnostics)
            => new ReferenceCompilationResult(null, new DiagnosticCollection(diagnostics));

        private static void AddRange(ICollection<Diagnostic> destination, DiagnosticCollection source)
        {
            for (var index = 0; index < source.Count; index++)
            {
                destination.Add(source[index]);
            }
        }

        private static bool ContainsError(IReadOnlyList<Diagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static CompiledHash ToCompiledHash(byte[] digest)
        {
            if (digest == null || digest.Length != 32)
            {
                throw new ArgumentException("A SHA-256 digest must contain exactly 32 bytes.", nameof(digest));
            }

            var characters = new char[64];
            for (var index = 0; index < digest.Length; index++)
            {
                var value = digest[index];
                characters[index * 2] = Hex(value >> 4);
                characters[(index * 2) + 1] = Hex(value & 15);
            }

            return new CompiledHash(new string(characters));
        }

        private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

        private sealed class Builder
        {
            private readonly TreeDocument _document;
            private readonly NodeRegistry _registry;
            private readonly ReferenceCompilerOptions _options;
            private readonly CompiledHash _semanticHash;
            private readonly CompiledHash _registryHash;
            private readonly List<NodeDocument> _nodes;
            private readonly Dictionary<NodeId, uint> _nodeIndices;
            private readonly Dictionary<NodeId, NodeRegistryEntry> _entries;

            internal Builder(
                TreeDocument document,
                NodeRegistry registry,
                ReferenceCompilerOptions options,
                CompiledHash semanticHash,
                CompiledHash registryHash)
            {
                _document = document;
                _registry = registry;
                _options = options;
                _semanticHash = semanticHash;
                _registryHash = registryHash;
                _nodes = OrderNodes(document);
                _nodeIndices = IndexNodes(_nodes);
                _entries = ResolveEntries(_nodes, registry);
            }

            internal CompiledProgram Build()
            {
                RequireTableCapacity(0, _nodes.Count, "node table");
                RequireTableCapacity(0, _document.Blackboard.Count, "blackboard table");
                var capabilities = ComputeCapabilities();
                var policyHash = _options.Policy.ComputeHash();
                var accessFlags = GatherBlackboardAccessFlags();
                var slotBuild = BuildBlackboardSlots(accessFlags);
                var observers = BuildObservers(slotBuild.SlotIndices);
                var layouts = BuildNodeLayouts();
                var tables = BuildNodeTables(slotBuild.SlotIndices);
                var debugMap = BuildDebugMap();
                var records = BuildNodeRecords(layouts, tables);

                var deterministicCompatible = (capabilities & (uint)NodeRegistryCapabilityFlags.NonDeterministic) == 0;
                var contentHash = CompiledContentHasher.Compute(
                    CompiledFormatVersion,
                    ExecutionSemanticsVersion,
                    _options.CompilerVersion,
                    _semanticHash,
                    _registryHash,
                    policyHash,
                    ReferenceCompilationPolicyCodec.FormatVersion,
                    _nodeIndices[_document.Root],
                    layouts.TotalMemorySize,
                    layouts.MaximumMemoryAlignment,
                    capabilities,
                    deterministicCompatible,
                    records,
                    tables.Children,
                    tables.ReadSlots,
                    tables.WriteSlots,
                    slotBuild.Records,
                    observers.Records,
                    observers.WatchedSlots,
                    layouts.ConfigBlob,
                    slotBuild.DefaultBlob,
                    debugMap);

                var header = new CompiledProgramHeader(
                    CompiledFormatVersion,
                    ExecutionSemanticsVersion,
                    _options.CompilerVersion,
                    _semanticHash,
                    _registryHash,
                    policyHash,
                    ReferenceCompilationPolicyCodec.FormatVersion,
                    contentHash,
                    _nodeIndices[_document.Root],
                    (uint)records.Count,
                    (uint)tables.Children.Count,
                    (uint)slotBuild.Records.Count,
                    (uint)debugMap.Count,
                    (uint)layouts.ConfigBlob.Length,
                    layouts.TotalMemorySize,
                    layouts.MaximumMemoryAlignment,
                    capabilities,
                    deterministicCompatible);

                return new CompiledProgram(
                    header,
                    records,
                    tables.Children,
                    tables.ReadSlots,
                    tables.WriteSlots,
                    slotBuild.Records,
                    observers.Records,
                    observers.WatchedSlots,
                    layouts.ConfigBlob,
                    slotBuild.DefaultBlob,
                    debugMap);
            }

            private uint ComputeCapabilities()
            {
                var result = NodeRegistryCapabilityFlags.None;
                for (var index = 0; index < _nodes.Count; index++)
                {
                    var node = _nodes[index];
                    var entry = _entries[node.Id];
                    if (!entry.HasReferenceHandlerBinding)
                    {
                        throw Failure(
                            ReferenceCompilerDiagnosticCodes.UnsupportedCapability,
                            "The selected node type has no Phase 1 reference-handler binding.",
                            NodePointer(node.Id) + "/type",
                            node.Id);
                    }

                    switch (entry.Manifest.ExecutionDomain)
                    {
                        case NodeExecutionDomain.Burst:
                            result |= NodeRegistryCapabilityFlags.Burst;
                            break;
                        case NodeExecutionDomain.Managed:
                            result |= NodeRegistryCapabilityFlags.Managed;
                            break;
                        case NodeExecutionDomain.MainThread:
                            result |= NodeRegistryCapabilityFlags.MainThread;
                            break;
                        default:
                            throw Failure(
                                ReferenceCompilerDiagnosticCodes.UnsupportedCapability,
                                "The node execution domain is unsupported.",
                                NodePointer(node.Id) + "/type",
                                node.Id);
                    }

                    if (!entry.Manifest.Deterministic) result |= NodeRegistryCapabilityFlags.NonDeterministic;
                    if (entry.Manifest.SideEffects.Count != 0) result |= NodeRegistryCapabilityFlags.SideEffects;
                    result |= NodeRegistryCapabilityFlags.ReferenceHandlerBindings;
                    if (entry.Source == NodeManifestSource.UserExtension) result |= NodeRegistryCapabilityFlags.UserExtensions;
                }

                return (uint)result;
            }

            private Dictionary<string, CompiledBlackboardAccessFlags> GatherBlackboardAccessFlags()
            {
                var result = new Dictionary<string, CompiledBlackboardAccessFlags>(StringComparer.Ordinal);
                for (var index = 0; index < _nodes.Count; index++)
                {
                    var manifest = _entries[_nodes[index].Id].Manifest;
                    AddAccesses(result, manifest.Reads, CompiledBlackboardAccessFlags.Read);
                    AddAccesses(result, manifest.Writes, CompiledBlackboardAccessFlags.Write);
                    var observer = _nodes[index].Observer;
                    if (observer != null)
                    {
                        AddAccesses(result, observer.WatchedKeys, CompiledBlackboardAccessFlags.Observed);
                    }
                }

                return result;
            }

            private BlackboardBuild BuildBlackboardSlots(
                IReadOnlyDictionary<string, CompiledBlackboardAccessFlags> accessFlags)
            {
                var keys = new List<BlackboardKeyDefinition>(_document.Blackboard);
                keys.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id, right.Id));
                var stableIds = new Dictionary<ulong, string>();
                var enumContractIds = new Dictionary<ulong, string>();
                var slotIndices = new Dictionary<string, uint>(StringComparer.Ordinal);
                var plans = new List<SlotPlan>(keys.Count);
                var scopeOffsets = new ulong[4];
                ulong defaultOffset = 0;

                for (var index = 0; index < keys.Count; index++)
                {
                    var key = keys[index];
                    if (key.Scope != BlackboardScope.Tree)
                    {
                        throw Failure(
                            ReferenceCompilerDiagnosticCodes.UnsupportedCapability,
                            "Phase 1 compilation supports only Tree-scope blackboard slots.",
                            BlackboardPointer(key.Id));
                    }

                    var descriptor = _options.ResolveBlackboardLayout(key.Type.RuntimeDescriptor);
                    if (!descriptor.IsValid || descriptor.Size <= 0 || descriptor.Alignment <= 0)
                    {
                        throw Failure(
                            ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure,
                            "The blackboard type has no valid runtime layout.",
                            BlackboardPointer(key.Id));
                    }

                    var stableId = _options.BlackboardKeyHash(key.Id);
                    if (stableId == 0
                        || stableIds.TryGetValue(stableId, out var existing) && !string.Equals(existing, key.Id, StringComparison.Ordinal))
                    {
                        throw Failure(
                            ReferenceCompilerDiagnosticCodes.StableIdentityCollision,
                            "Blackboard key stable identities collide.",
                            BlackboardPointer(key.Id));
                    }

                    stableIds[stableId] = key.Id;
                    if (key.Type.ValueType == BlackboardValueType.Enum32)
                    {
                        if (key.Type.EnumContractId == 0
                            || enumContractIds.TryGetValue(key.Type.EnumContractId, out var existingContract)
                            && !string.Equals(existingContract, key.Type.EnumContract, StringComparison.Ordinal))
                        {
                            throw Failure(
                                ReferenceCompilerDiagnosticCodes.StableIdentityCollision,
                                "Enum32 contract stable identities collide.",
                                BlackboardPointer(key.Id));
                        }

                        enumContractIds[key.Type.EnumContractId] = key.Type.EnumContract;
                    }

                    var size = checked((uint)descriptor.Size);
                    var alignment = checked((uint)descriptor.Alignment);
                    var scopeIndex = (int)key.Scope;
                    var slotOffset = Align(scopeOffsets[scopeIndex], alignment, "blackboard slot layout");
                    scopeOffsets[scopeIndex] = Add(slotOffset, size, "blackboard slot layout");
                    var alignedDefault = Align(defaultOffset, alignment, "blackboard default-value layout");
                    defaultOffset = Add(alignedDefault, size, "blackboard default-value layout");
                    RequireManagedBlobSize(defaultOffset, "blackboard default-value blob");

                    byte[] encodedDefault = null;
                    if (key.DefaultValue != null)
                    {
                        if (!key.DefaultValue.TryGetRuntimeValue(out var runtimeValue))
                        {
                            throw Failure(
                                ReferenceCompilerDiagnosticCodes.DefaultValuePacking,
                                "The blackboard default has no supported Phase 1 binary representation.",
                                BlackboardPointer(key.Id));
                        }

                        encodedDefault = CompiledBlackboardValueEncoder.Encode(runtimeValue);
                        if (encodedDefault.Length != descriptor.Size)
                        {
                            throw Failure(
                                ReferenceCompilerDiagnosticCodes.DefaultValuePacking,
                                "The encoded blackboard default does not match its declared slot size.",
                                BlackboardPointer(key.Id));
                        }

                        if (key.Type.ValueType == BlackboardValueType.Enum32
                            && ReadUInt64(encodedDefault, 0) != key.Type.EnumContractId)
                        {
                            throw Failure(
                                ReferenceCompilerDiagnosticCodes.DefaultValuePacking,
                                "The encoded Enum32 default contract does not match its declared key contract.",
                                BlackboardPointer(key.Id));
                        }
                    }
                    else if (key.Type.ValueType == BlackboardValueType.Enum32)
                    {
                        encodedDefault = CompiledBlackboardValueEncoder.Encode(
                            BlackboardValue.FromEnum32(new Enum32Value(key.Type.EnumContractId, 0)));
                    }

                    accessFlags.TryGetValue(key.Id, out var flags);
                    plans.Add(new SlotPlan(
                        stableId,
                        descriptor,
                        key.Type.EnumContractId,
                        key.Scope,
                        checked((uint)slotOffset),
                        checked((uint)alignedDefault),
                        flags,
                        encodedDefault));
                    slotIndices.Add(key.Id, (uint)index);
                }

                var defaultBlob = new byte[(int)defaultOffset];
                var records = new List<CompiledBlackboardSlotRecord>(plans.Count);
                for (var index = 0; index < plans.Count; index++)
                {
                    var plan = plans[index];
                    if (plan.DefaultBytes != null)
                    {
                        Buffer.BlockCopy(plan.DefaultBytes, 0, defaultBlob, (int)plan.DefaultOffset, plan.DefaultBytes.Length);
                    }

                    records.Add(new CompiledBlackboardSlotRecord(
                        plan.StableKeyId,
                        plan.Descriptor.TypeId,
                        plan.Descriptor.Version,
                        plan.EnumContractId,
                        plan.Scope,
                        plan.Offset,
                        checked((uint)plan.Descriptor.Size),
                        checked((uint)plan.Descriptor.Alignment),
                        plan.DefaultOffset,
                        plan.AccessFlags));
                }

                return new BlackboardBuild(records, slotIndices, defaultBlob);
            }

            private ObserverBuild BuildObservers(IReadOnlyDictionary<string, uint> slotIndices)
            {
                var parents = BuildParentMap();
                var records = new List<CompiledObserverRecord>();
                var watchedSlots = new List<uint>();
                for (var index = 0; index < _nodes.Count; index++)
                {
                    var node = _nodes[index];
                    if (node.Observer == null)
                    {
                        continue;
                    }

                    if (!parents.TryGetValue(node.Id, out var parentId))
                    {
                        throw Failure(
                            ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure,
                            "An observer node has no owning reactive composite.",
                            NodePointer(node.Id) + "/observer",
                            node.Id);
                    }

                    var watchedIds = new List<string>(node.Observer.WatchedKeys);
                    watchedIds.Sort((left, right) =>
                    {
                        var comparison = StableHash.Fnv1A64(left).CompareTo(StableHash.Fnv1A64(right));
                        return comparison != 0 ? comparison : Utf8OrdinalComparer.Instance.Compare(left, right);
                    });
                    RequireTableCapacity(watchedSlots.Count, watchedIds.Count, "observer watched-slot table");
                    var range = new CompiledRange((uint)watchedSlots.Count, (uint)watchedIds.Count);
                    for (var watchedIndex = 0; watchedIndex < watchedIds.Count; watchedIndex++)
                    {
                        watchedSlots.Add(slotIndices[watchedIds[watchedIndex]]);
                    }

                    records.Add(new CompiledObserverRecord(
                        (uint)index,
                        _nodeIndices[parentId],
                        ObserverMode(node.Observer.Mode),
                        range));
                }

                return new ObserverBuild(records, watchedSlots);
            }

            private NodeLayouts BuildNodeLayouts()
            {
                var configOffsets = new uint[_nodes.Count];
                var configSizes = new uint[_nodes.Count];
                var configAlignments = new uint[_nodes.Count];
                var memoryOffsets = new uint[_nodes.Count];
                var memorySizes = new uint[_nodes.Count];
                var memoryAlignments = new uint[_nodes.Count];
                ulong configCursor = 0;
                ulong memoryCursor = 0;
                uint maximumAlignment = 1;

                for (var index = 0; index < _nodes.Count; index++)
                {
                    var manifest = _entries[_nodes[index].Id].Manifest;
                    var configuration = manifest.Configuration;
                    if (configuration.Size != 0)
                    {
                        var offset = Align(configCursor, configuration.Alignment, "node configuration layout");
                        configCursor = Add(offset, configuration.Size, "node configuration layout");
                        RequireManagedBlobSize(configCursor, "node configuration blob");
                        configOffsets[index] = checked((uint)offset);
                        configSizes[index] = configuration.Size;
                        configAlignments[index] = configuration.Alignment;
                    }
                    else configAlignments[index] = 1;

                    var memory = manifest.Memory;
                    if (memory.Size == 0)
                    {
                        memoryAlignments[index] = 1;
                        continue;
                    }

                    var memoryOffset = Align(memoryCursor, memory.Alignment, "node instance-memory layout");
                    memoryCursor = Add(memoryOffset, memory.Size, "node instance-memory layout");
                    memoryOffsets[index] = checked((uint)memoryOffset);
                    memorySizes[index] = memory.Size;
                    memoryAlignments[index] = memory.Alignment;
                    if (memory.Alignment > maximumAlignment) maximumAlignment = memory.Alignment;
                }

                memoryCursor = Align(memoryCursor, maximumAlignment, "node instance-memory layout");
                if (memoryCursor > uint.MaxValue)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        "Node instance-memory layout exceeds the 32-bit compiled-program limit.");
                }

                var configBlob = new byte[(int)configCursor];
                for (var index = 0; index < _nodes.Count; index++)
                {
                    if (configSizes[index] != 0)
                    {
                        PackConfiguration(
                            _nodes[index],
                            _entries[_nodes[index].Id].Manifest,
                            configBlob,
                            configOffsets[index]);
                    }
                }

                return new NodeLayouts(
                    configOffsets,
                    configSizes,
                    configAlignments,
                    memoryOffsets,
                    memorySizes,
                    memoryAlignments,
                    configBlob,
                    (uint)memoryCursor,
                    maximumAlignment);
            }

            private NodeTables BuildNodeTables(IReadOnlyDictionary<string, uint> slotIndices)
            {
                var children = new List<uint>();
                var reads = new List<uint>();
                var writes = new List<uint>();
                var childRanges = new CompiledRange[_nodes.Count];
                var readRanges = new CompiledRange[_nodes.Count];
                var writeRanges = new CompiledRange[_nodes.Count];

                for (var index = 0; index < _nodes.Count; index++)
                {
                    var node = _nodes[index];
                    RequireTableCapacity(children.Count, node.Children.Count, "child-index table");
                    childRanges[index] = new CompiledRange((uint)children.Count, (uint)node.Children.Count);
                    for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
                    {
                        children.Add(_nodeIndices[node.Children[childIndex]]);
                    }

                    var manifest = _entries[node.Id].Manifest;
                    AppendAccessTable(manifest.Reads, slotIndices, reads, out readRanges[index]);
                    AppendAccessTable(manifest.Writes, slotIndices, writes, out writeRanges[index]);
                }

                return new NodeTables(children, reads, writes, childRanges, readRanges, writeRanges);
            }

            private List<CompiledDebugMapEntry> BuildDebugMap()
            {
                var result = new List<CompiledDebugMapEntry>(_nodes.Count);
                for (var index = 0; index < _nodes.Count; index++)
                {
                    var node = _nodes[index];
                    result.Add(new CompiledDebugMapEntry(
                        (uint)index,
                        node.Id,
                        _options.SourceId,
                        _options.StripDisplayMetadata ? null : node.DisplayName));
                }

                return result;
            }

            private List<CompiledNodeRecord> BuildNodeRecords(NodeLayouts layouts, NodeTables tables)
            {
                var result = new List<CompiledNodeRecord>(_nodes.Count);
                for (var index = 0; index < _nodes.Count; index++)
                {
                    var entry = _entries[_nodes[index].Id];
                    result.Add(new CompiledNodeRecord(
                        entry.NumericTypeId,
                        entry.Manifest.Version,
                        layouts.ConfigOffsets[index],
                        layouts.ConfigSizes[index],
                        layouts.ConfigAlignments[index],
                        layouts.MemoryOffsets[index],
                        layouts.MemorySizes[index],
                        layouts.MemoryAlignments[index],
                        entry.Manifest.Memory.Lifetime,
                        tables.ChildRanges[index],
                        NodeFlags(entry.Manifest.ExecutionDomain),
                        (uint)index,
                        tables.ReadRanges[index],
                        tables.WriteRanges[index]));
                }

                return result;
            }

            private Dictionary<NodeId, NodeId> BuildParentMap()
            {
                var result = new Dictionary<NodeId, NodeId>();
                for (var index = 0; index < _nodes.Count; index++)
                {
                    var parent = _nodes[index];
                    for (var childIndex = 0; childIndex < parent.Children.Count; childIndex++)
                    {
                        result[parent.Children[childIndex]] = parent.Id;
                    }
                }

                return result;
            }

            private static void PackConfiguration(
                NodeDocument node,
                NodeManifest manifest,
                byte[] blob,
                uint baseOffset)
            {
                for (var fieldIndex = 0; fieldIndex < manifest.Configuration.Fields.Count; fieldIndex++)
                {
                    var field = manifest.Configuration.Fields[fieldIndex];
                    if (node.Parameters == null || !node.Parameters.TryGetValue(field.ParameterName, out var value))
                    {
                        continue;
                    }

                    var contract = FindParameter(manifest.Parameters, field.ParameterName);
                    var offset = checked((int)(baseOffset + field.Offset));
                    switch (contract.Type)
                    {
                        case NodeParameterType.Boolean:
                            if (!value.TryGetBoolean(out var boolean)) throw PackingFailure(node, field.ParameterName);
                            blob[offset] = boolean ? (byte)1 : (byte)0;
                            break;
                        case NodeParameterType.UInt32:
                            if (!TryGetUnsigned(value, uint.MaxValue, out var uint32)) throw PackingFailure(node, field.ParameterName);
                            WriteUInt32(blob, offset, (uint)uint32);
                            break;
                        case NodeParameterType.UInt64:
                            if (!TryGetUnsigned(value, ulong.MaxValue, out var uint64)) throw PackingFailure(node, field.ParameterName);
                            WriteUInt64(blob, offset, uint64);
                            break;
                        case NodeParameterType.StringEnum:
                            if (!value.TryGetString(out var text)) throw PackingFailure(node, field.ParameterName);
                            var enumIndex = IndexOf(contract.AllowedValues, text);
                            if (enumIndex < 0 || enumIndex > byte.MaxValue) throw PackingFailure(node, field.ParameterName);
                            blob[offset] = (byte)enumIndex;
                            break;
                        default:
                            throw PackingFailure(node, field.ParameterName);
                    }
                }
            }

            private void AppendAccessTable(
                IReadOnlyList<string> source,
                IReadOnlyDictionary<string, uint> slotIndices,
                ICollection<uint> destination,
                out CompiledRange range)
            {
                var keys = new List<string>(source);
                keys.Sort(Utf8OrdinalComparer.Instance.Compare);
                RequireTableCapacity(destination.Count, keys.Count, "blackboard access table");
                var offset = (uint)destination.Count;
                for (var index = 0; index < keys.Count; index++)
                {
                    destination.Add(slotIndices[keys[index]]);
                }

                range = new CompiledRange(offset, (uint)keys.Count);
            }

            private static void AddAccesses(
                IDictionary<string, CompiledBlackboardAccessFlags> result,
                IReadOnlyList<string> keys,
                CompiledBlackboardAccessFlags flag)
            {
                for (var index = 0; index < keys.Count; index++)
                {
                    result.TryGetValue(keys[index], out var existing);
                    result[keys[index]] = existing | flag;
                }
            }

            private static List<NodeDocument> OrderNodes(TreeDocument document)
            {
                var byId = new Dictionary<NodeId, NodeDocument>();
                for (var index = 0; index < document.Nodes.Count; index++)
                {
                    byId.Add(document.Nodes[index].Id, document.Nodes[index]);
                }

                var result = new List<NodeDocument>(document.Nodes.Count);
                var visited = new HashSet<NodeId>();
                var stack = new Stack<NodeId>();
                stack.Push(document.Root);
                while (stack.Count != 0)
                {
                    var id = stack.Pop();
                    if (!visited.Add(id)) continue;
                    var node = byId[id];
                    result.Add(node);
                    for (var childIndex = node.Children.Count - 1; childIndex >= 0; childIndex--)
                    {
                        stack.Push(node.Children[childIndex]);
                    }
                }

                var unreachable = new List<NodeDocument>();
                for (var index = 0; index < document.Nodes.Count; index++)
                {
                    if (!visited.Contains(document.Nodes[index].Id)) unreachable.Add(document.Nodes[index]);
                }

                unreachable.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id.Value, right.Id.Value));
                result.AddRange(unreachable);
                return result;
            }

            private static Dictionary<NodeId, uint> IndexNodes(IReadOnlyList<NodeDocument> nodes)
            {
                var result = new Dictionary<NodeId, uint>();
                for (var index = 0; index < nodes.Count; index++) result.Add(nodes[index].Id, (uint)index);
                return result;
            }

            private static Dictionary<NodeId, NodeRegistryEntry> ResolveEntries(
                IReadOnlyList<NodeDocument> nodes,
                NodeRegistry registry)
            {
                var result = new Dictionary<NodeId, NodeRegistryEntry>();
                for (var index = 0; index < nodes.Count; index++)
                {
                    if (!registry.TryGet(nodes[index].TypeId, out var entry))
                    {
                        throw Failure(
                            ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure,
                            "A validated node type is missing from the selected registry.",
                            NodePointer(nodes[index].Id) + "/type",
                            nodes[index].Id);
                    }

                    result.Add(nodes[index].Id, entry);
                }

                return result;
            }

            private static CompiledNodeFlags NodeFlags(NodeExecutionDomain domain)
            {
                switch (domain)
                {
                    case NodeExecutionDomain.Burst: return CompiledNodeFlags.BurstDomain;
                    case NodeExecutionDomain.Managed: return CompiledNodeFlags.ManagedDomain;
                    case NodeExecutionDomain.MainThread: return CompiledNodeFlags.MainThreadDomain;
                    default: throw new ArgumentOutOfRangeException(nameof(domain));
                }
            }

            private static CompiledObserverMode ObserverMode(string mode)
            {
                switch (mode)
                {
                    case "self": return CompiledObserverMode.Self;
                    case "lower-priority": return CompiledObserverMode.LowerPriority;
                    case "both": return CompiledObserverMode.Both;
                    default: throw new ArgumentOutOfRangeException(nameof(mode));
                }
            }

            private static NodeParameterContract FindParameter(
                IReadOnlyList<NodeParameterContract> parameters,
                string name)
            {
                for (var index = 0; index < parameters.Count; index++)
                {
                    if (parameters[index].Name == name) return parameters[index];
                }

                throw new InvalidOperationException("The validated manifest has no matching parameter contract.");
            }

            private static bool TryGetUnsigned(SemanticValue value, ulong maximum, out ulong result)
            {
                if (value.TryGetUInt64(out result)) return result <= maximum;
                if (value.TryGetInt64(out var signed) && signed >= 0)
                {
                    result = (ulong)signed;
                    return result <= maximum;
                }

                result = 0;
                return false;
            }

            private static int IndexOf(IReadOnlyList<string> values, string value)
            {
                for (var index = 0; index < values.Count; index++)
                {
                    if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
                }

                return -1;
            }

            private static ulong Align(ulong value, uint alignment, string label)
            {
                if (alignment == 0 || (alignment & (alignment - 1)) != 0)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        "Invalid alignment in " + label + ".");
                }

                var mask = (ulong)alignment - 1;
                if (value > ulong.MaxValue - mask)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        "Overflow in " + label + ".");
                }

                var result = (value + mask) & ~mask;
                if (result >= CompiledIndex.Invalid)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        label + " exceeds the 32-bit compiled-program limit.");
                }

                return result;
            }

            private static ulong Add(ulong offset, uint size, string label)
            {
                var result = offset + size;
                if (result > uint.MaxValue)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        label + " exceeds the 32-bit compiled-program limit.");
                }

                return result;
            }

            private static void RequireManagedBlobSize(ulong size, string label)
            {
                if (size > int.MaxValue)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        label + " exceeds the Phase 1 managed-container limit.");
                }
            }

            private void RequireTableCapacity(int existing, int addition, string label)
            {
                var total = (ulong)(uint)existing + (uint)addition;
                if (total > _options.TableElementLimit || total >= CompiledIndex.Invalid)
                {
                    throw Failure(
                        ReferenceCompilerDiagnosticCodes.LayoutOverflow,
                        label + " exceeds the configured compiled-program limit.");
                }
            }

            private static void WriteUInt32(byte[] destination, int offset, uint value)
            {
                destination[offset] = (byte)value;
                destination[offset + 1] = (byte)(value >> 8);
                destination[offset + 2] = (byte)(value >> 16);
                destination[offset + 3] = (byte)(value >> 24);
            }

            private static void WriteUInt64(byte[] destination, int offset, ulong value)
            {
                WriteUInt32(destination, offset, (uint)value);
                WriteUInt32(destination, offset + 4, (uint)(value >> 32));
            }

            private static ulong ReadUInt64(byte[] source, int offset)
            {
                ulong value = 0;
                for (var index = 0; index < 8; index++) value |= (ulong)source[offset + index] << (index * 8);
                return value;
            }

            private static CompilationFailureException PackingFailure(NodeDocument node, string parameter)
            {
                return Failure(
                    ReferenceCompilerDiagnosticCodes.ConfigurationPacking,
                    "A validated node parameter could not be packed into its manifest configuration layout.",
                    NodePointer(node.Id) + "/parameters/" + Escape(parameter),
                    node.Id);
            }

            private static CompilationFailureException Failure(
                DiagnosticCode code,
                string message,
                string pointer = null,
                NodeId nodeId = default)
                => new CompilationFailureException(code, message, pointer, nodeId);

            private static string NodePointer(NodeId id) => "/nodes/" + Escape(id.Value);

            private static string BlackboardPointer(string id) => "/blackboard/" + Escape(id);

            private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
        }

        private sealed class CompilationFailureException : Exception
        {
            internal CompilationFailureException(DiagnosticCode code, string message, string jsonPointer, NodeId nodeId)
                : base(message)
            {
                Code = code;
                JsonPointer = jsonPointer;
                NodeId = nodeId;
            }

            internal DiagnosticCode Code { get; }
            internal string JsonPointer { get; }
            internal NodeId NodeId { get; }
        }

        private sealed class SlotPlan
        {
            internal SlotPlan(
                ulong stableKeyId,
                BlackboardTypeDescriptor descriptor,
                ulong enumContractId,
                BlackboardScope scope,
                uint offset,
                uint defaultOffset,
                CompiledBlackboardAccessFlags accessFlags,
                byte[] defaultBytes)
            {
                StableKeyId = stableKeyId;
                Descriptor = descriptor;
                EnumContractId = enumContractId;
                Scope = scope;
                Offset = offset;
                DefaultOffset = defaultOffset;
                AccessFlags = accessFlags;
                DefaultBytes = defaultBytes;
            }

            internal ulong StableKeyId { get; }
            internal BlackboardTypeDescriptor Descriptor { get; }
            internal ulong EnumContractId { get; }
            internal BlackboardScope Scope { get; }
            internal uint Offset { get; }
            internal uint DefaultOffset { get; }
            internal CompiledBlackboardAccessFlags AccessFlags { get; }
            internal byte[] DefaultBytes { get; }
        }

        private sealed class BlackboardBuild
        {
            internal BlackboardBuild(
                List<CompiledBlackboardSlotRecord> records,
                Dictionary<string, uint> slotIndices,
                byte[] defaultBlob)
            {
                Records = records;
                SlotIndices = slotIndices;
                DefaultBlob = defaultBlob;
            }

            internal List<CompiledBlackboardSlotRecord> Records { get; }
            internal Dictionary<string, uint> SlotIndices { get; }
            internal byte[] DefaultBlob { get; }
        }

        private sealed class ObserverBuild
        {
            internal ObserverBuild(List<CompiledObserverRecord> records, List<uint> watchedSlots)
            {
                Records = records;
                WatchedSlots = watchedSlots;
            }

            internal List<CompiledObserverRecord> Records { get; }
            internal List<uint> WatchedSlots { get; }
        }

        private sealed class NodeLayouts
        {
            internal NodeLayouts(
                uint[] configOffsets,
                uint[] configSizes,
                uint[] configAlignments,
                uint[] memoryOffsets,
                uint[] memorySizes,
                uint[] memoryAlignments,
                byte[] configBlob,
                uint totalMemorySize,
                uint maximumMemoryAlignment)
            {
                ConfigOffsets = configOffsets;
                ConfigSizes = configSizes;
                ConfigAlignments = configAlignments;
                MemoryOffsets = memoryOffsets;
                MemorySizes = memorySizes;
                MemoryAlignments = memoryAlignments;
                ConfigBlob = configBlob;
                TotalMemorySize = totalMemorySize;
                MaximumMemoryAlignment = maximumMemoryAlignment;
            }

            internal uint[] ConfigOffsets { get; }
            internal uint[] ConfigSizes { get; }
            internal uint[] ConfigAlignments { get; }
            internal uint[] MemoryOffsets { get; }
            internal uint[] MemorySizes { get; }
            internal uint[] MemoryAlignments { get; }
            internal byte[] ConfigBlob { get; }
            internal uint TotalMemorySize { get; }
            internal uint MaximumMemoryAlignment { get; }
        }

        private sealed class NodeTables
        {
            internal NodeTables(
                List<uint> children,
                List<uint> readSlots,
                List<uint> writeSlots,
                CompiledRange[] childRanges,
                CompiledRange[] readRanges,
                CompiledRange[] writeRanges)
            {
                Children = children;
                ReadSlots = readSlots;
                WriteSlots = writeSlots;
                ChildRanges = childRanges;
                ReadRanges = readRanges;
                WriteRanges = writeRanges;
            }

            internal List<uint> Children { get; }
            internal List<uint> ReadSlots { get; }
            internal List<uint> WriteSlots { get; }
            internal CompiledRange[] ChildRanges { get; }
            internal CompiledRange[] ReadRanges { get; }
            internal CompiledRange[] WriteRanges { get; }
        }
    }
}
