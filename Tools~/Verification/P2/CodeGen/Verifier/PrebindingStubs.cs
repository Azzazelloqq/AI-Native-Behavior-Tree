using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AIBT
{
    public enum NodeMemoryLifetime : byte { Activation = 0, Instance = 1 }
    public enum NodeStatus : byte { Success = 0, Failure = 1, Running = 2 }

    public readonly struct CompiledHash : IEquatable<CompiledHash>
    {
        public CompiledHash(string hexadecimalValue) => HexadecimalValue = hexadecimalValue;
        public string HexadecimalValue { get; }
        public bool IsValid => HexadecimalValue != null && HexadecimalValue.Length == 64;
        public bool Equals(CompiledHash other) => HexadecimalValue == other.HexadecimalValue;
        public override bool Equals(object? obj) => obj is CompiledHash other && Equals(other);
        public override int GetHashCode() => HexadecimalValue?.GetHashCode() ?? 0;
    }

    public enum BlackboardScope : byte
    {
        NodeLocal = 0,
        Tree = 1,
        Agent = 2,
        Shared = 3,
    }

    public static class StableHash
    {
        public static ulong Fnv1A64(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = 14695981039346656037UL;
            for (var index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash = unchecked(hash * 1099511628211UL);
            }
            return hash;
        }
    }

    public readonly struct RegisteredUnmanagedTypeDescriptor : IEquatable<RegisteredUnmanagedTypeDescriptor>
    {
        public RegisteredUnmanagedTypeDescriptor(uint token)
            : this(token, 1u, 1, 1, token, token)
        {
            Token = token;
        }

        public RegisteredUnmanagedTypeDescriptor(
            ulong typeId,
            uint version,
            int size,
            int alignment,
            ulong equalityContractId,
            ulong canonicalSchemaId = 0,
            uint migrationSourceVersion = 0,
            ulong migrationContractId = 0)
        {
            Token = 0;
            TypeId = typeId;
            Version = version;
            Size = size;
            Alignment = alignment;
            EqualityContractId = equalityContractId;
            CanonicalSchemaId = canonicalSchemaId;
            MigrationSourceVersion = migrationSourceVersion;
            MigrationContractId = migrationContractId;
        }

        public uint Token { get; }
        public ulong TypeId { get; }
        public uint Version { get; }
        public int Size { get; }
        public int Alignment { get; }
        public ulong EqualityContractId { get; }
        public ulong CanonicalSchemaId { get; }
        public uint MigrationSourceVersion { get; }
        public ulong MigrationContractId { get; }
        public bool HasCanonicalSchema => CanonicalSchemaId != 0;
        public bool HasMigration => MigrationSourceVersion != 0;
        public bool IsValid => TypeId != 0 && Version != 0 && Size > 0 && Alignment > 0 && EqualityContractId != 0;

        public bool Equals(RegisteredUnmanagedTypeDescriptor other)
            => Token == other.Token
                && TypeId == other.TypeId
                && Version == other.Version
                && Size == other.Size
                && Alignment == other.Alignment
                && EqualityContractId == other.EqualityContractId
                && CanonicalSchemaId == other.CanonicalSchemaId
                && MigrationSourceVersion == other.MigrationSourceVersion
                && MigrationContractId == other.MigrationContractId;

        public override bool Equals(object? obj)
            => obj is RegisteredUnmanagedTypeDescriptor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TypeId, Version, Size, Alignment);
        public static bool operator ==(RegisteredUnmanagedTypeDescriptor left, RegisteredUnmanagedTypeDescriptor right) => left.Equals(right);
        public static bool operator !=(RegisteredUnmanagedTypeDescriptor left, RegisteredUnmanagedTypeDescriptor right) => !left.Equals(right);
    }
}

namespace AIBT.Authoring
{
    public enum NodeBehaviorKind : byte { Composite = 0, Decorator = 1, Condition = 2, Action = 3 }
    public enum NodeCancellationMode : byte { NotApplicable = 0, AbortOnly = 1, Command = 2 }
    public enum NodeCostHint : byte { Trivial = 0, Low = 1, Medium = 2, High = 3, Variable = 4 }

    internal static class NodeTypeIdRules
    {
        internal static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Contains(".");
    }

    public enum GeneratedFieldEncoding : byte
    {
        Bool8 = 0, Int8 = 1, UInt8 = 2, Int16LE = 3, UInt16LE = 4,
        Int32LE = 5, UInt32LE = 6, Int64LE = 7, UInt64LE = 8,
        Float32BitsLE = 9, Float64BitsLE = 10, FixedBytes = 11,
        GeneratedHandle = 12, Registered = 13,
    }

    public enum GeneratedBindingKind : byte
    {
        BlackboardRead = 0, BlackboardWrite = 1, BlackboardReadWrite = 2,
        SnapshotRead = 3, EffectCommand = 4, AsyncOperation = 5, Completion = 6,
    }

    public enum GeneratedTypeRole : byte
    {
        Value = 0, EffectPayload = 1, AsyncStartPayload = 2,
        AsyncCancelPayload = 3, CompletionPayload = 4,
    }

    [Flags]
    public enum GeneratedPhaseCapability : byte
    {
        None = 0, Execute = 1, Cancel = 2, Completion = 4,
    }

    internal sealed class Utf8OrdinalComparer : IComparer<string>
    {
        internal static Utf8OrdinalComparer Instance { get; } = new Utf8OrdinalComparer();
        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            var count = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < count; index++)
            {
                var comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0) return comparison;
            }
            return leftBytes.Length.CompareTo(rightBytes.Length);
        }
    }

    internal static class GeneratedNodeMetadata
    {
        internal const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";
        internal const ulong CanonicalBytesEqualityContractId = 0x69e3a80e385e338eUL;
    }

    internal static class GeneratedTypeRecordHash
    {
        internal static bool IsHash(string? value)
        {
            if (value == null || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character < '0' || character > '9') && (character < 'a' || character > 'f')) return false;
            }
            return true;
        }
    }

    internal static class GeneratedTypeLayoutRules
    {
        internal static bool TryBuiltIn(string id, out uint size, out byte alignment, out GeneratedFieldEncoding encoding)
        {
            switch (id)
            {
                case "Bool": size = 1; alignment = 1; encoding = GeneratedFieldEncoding.Bool8; return true;
                case "Int8": size = 1; alignment = 1; encoding = GeneratedFieldEncoding.Int8; return true;
                case "UInt8": size = 1; alignment = 1; encoding = GeneratedFieldEncoding.UInt8; return true;
                case "Int16": size = 2; alignment = 2; encoding = GeneratedFieldEncoding.Int16LE; return true;
                case "UInt16": size = 2; alignment = 2; encoding = GeneratedFieldEncoding.UInt16LE; return true;
                case "Int32": size = 4; alignment = 4; encoding = GeneratedFieldEncoding.Int32LE; return true;
                case "UInt32": size = 4; alignment = 4; encoding = GeneratedFieldEncoding.UInt32LE; return true;
                case "Int64": size = 8; alignment = 8; encoding = GeneratedFieldEncoding.Int64LE; return true;
                case "UInt64": size = 8; alignment = 8; encoding = GeneratedFieldEncoding.UInt64LE; return true;
                case "Float32": size = 4; alignment = 4; encoding = GeneratedFieldEncoding.Float32BitsLE; return true;
                case "Float64": size = 8; alignment = 8; encoding = GeneratedFieldEncoding.Float64BitsLE; return true;
                case "Float2": size = 8; alignment = 4; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "Float3": size = 12; alignment = 4; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "Quaternion": size = 16; alignment = 4; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "AgentId": case "EntityId": size = 8; alignment = 8; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "OperationId": size = 24; alignment = 8; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "AssetId": size = 32; alignment = 8; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "FixedString32": size = 32; alignment = 2; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "FixedString64": size = 64; alignment = 2; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "FixedString128": size = 128; alignment = 2; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                case "FixedString512": size = 512; alignment = 2; encoding = GeneratedFieldEncoding.FixedBytes; return true;
                default: size = 0; alignment = 0; encoding = default; return false;
            }
        }
    }

    public sealed class GeneratedTypeRecord
    {
        public GeneratedTypeRecord(
            GeneratedTypeRole role,
            string canonicalTypeId,
            uint version,
            string? schemaHash = null,
            AIBT.RegisteredUnmanagedTypeDescriptor registeredDescriptor = default)
        {
            Role = role;
            CanonicalTypeId = canonicalTypeId;
            NumericTypeId = AIBT.StableHash.Fnv1A64(canonicalTypeId);
            Version = version;
            SchemaHash = schemaHash ?? GeneratedNodeMetadata.ZeroHash;
            RegisteredDescriptor = registeredDescriptor;
        }

        public GeneratedTypeRole Role { get; }
        public string CanonicalTypeId { get; }
        public ulong NumericTypeId { get; }
        public uint Version { get; }
        public string SchemaHash { get; }
        public AIBT.RegisteredUnmanagedTypeDescriptor RegisteredDescriptor { get; }
    }

    public sealed class GeneratedStorageField
    {
        public GeneratedStorageField(
            string fieldId,
            string valueTypeId,
            uint valueTypeVersion,
            uint offset,
            uint size,
            GeneratedFieldEncoding encoding,
            string registeredSchemaHash = "",
            AIBT.RegisteredUnmanagedTypeDescriptor registeredDescriptor = default)
        {
            FieldId = fieldId;
            NumericFieldId = AIBT.StableHash.Fnv1A64(fieldId);
            ValueTypeId = valueTypeId;
            ValueTypeVersion = valueTypeVersion;
            Offset = offset;
            Size = size;
            Alignment = AlignmentFor(valueTypeId, size);
            Encoding = encoding;
            RegisteredSchemaHash = string.IsNullOrEmpty(registeredSchemaHash)
                ? GeneratedNodeMetadata.ZeroHash
                : registeredSchemaHash;
            RegisteredDescriptor = registeredDescriptor;
        }

        public GeneratedStorageField(
            string fieldId,
            string valueTypeId,
            uint valueTypeVersion,
            uint size,
            byte alignment,
            GeneratedFieldEncoding encoding,
            string? registeredSchemaHash = null,
            string? bindingId = null,
            AIBT.RegisteredUnmanagedTypeDescriptor registeredDescriptor = default)
        {
            FieldId = fieldId;
            NumericFieldId = AIBT.StableHash.Fnv1A64(fieldId);
            ValueTypeId = valueTypeId;
            ValueTypeVersion = valueTypeVersion;
            Size = size;
            Alignment = alignment;
            Encoding = encoding;
            RegisteredSchemaHash = registeredSchemaHash ?? GeneratedNodeMetadata.ZeroHash;
            BindingId = bindingId;
            RegisteredDescriptor = registeredDescriptor;
        }

        public string FieldId { get; }
        public ulong NumericFieldId { get; }
        public string ValueTypeId { get; }
        public uint ValueTypeVersion { get; }
        public uint Offset { get; internal set; }
        public uint Size { get; }
        public byte Alignment { get; }
        public GeneratedFieldEncoding Encoding { get; }
        public string RegisteredSchemaHash { get; }
        public string? BindingId { get; }
        public AIBT.RegisteredUnmanagedTypeDescriptor RegisteredDescriptor { get; }

        internal static bool ValidAlignment(byte value)
            => value == 1 || value == 2 || value == 4 || value == 8 || value == 16;

        private static byte AlignmentFor(string valueTypeId, uint size)
        {
            if (GeneratedTypeLayoutRules.TryBuiltIn(valueTypeId, out _, out var alignment, out _)) return alignment;
            if (size % 8u == 0) return 8;
            if (size % 4u == 0) return 4;
            if (size % 2u == 0) return 2;
            return 1;
        }
    }

    public sealed class GeneratedBindingDescriptor
    {
        public GeneratedBindingDescriptor(
            string bindingId,
            GeneratedBindingKind kind,
            AIBT.BlackboardScope scope,
            GeneratedPhaseCapability phaseCapabilities,
            IReadOnlyList<GeneratedTypeRecord> types,
            uint ordinal = 0)
        {
            BindingId = bindingId;
            NumericBindingId = AIBT.StableHash.Fnv1A64(bindingId);
            Kind = kind;
            Scope = scope;
            PhaseCapabilities = phaseCapabilities;
            Types = types;
            Ordinal = ordinal;
        }

        public string BindingId { get; }
        public ulong NumericBindingId { get; }
        public GeneratedBindingKind Kind { get; }
        public AIBT.BlackboardScope Scope { get; }
        public GeneratedPhaseCapability PhaseCapabilities { get; }
        public uint Ordinal { get; internal set; }
        public IReadOnlyList<GeneratedTypeRecord> Types { get; }
        public bool IsBlackboard => Kind <= GeneratedBindingKind.BlackboardReadWrite;
    }

    public sealed class ManifestField
    {
        public ManifestField(string parameterName, uint offset, uint size, byte alignment, bool isGeneratedHandle)
        {
            ParameterName = parameterName;
            Offset = offset;
            Size = size;
            Alignment = alignment;
            IsGeneratedHandle = isGeneratedHandle;
        }
        public string ParameterName { get; }
        public uint Offset { get; }
        public uint Size { get; }
        public byte Alignment { get; }
        public bool IsGeneratedHandle { get; }
    }

    public sealed class ManifestLayout
    {
        public ManifestLayout(IReadOnlyList<ManifestField> fields, uint size, byte alignment)
        {
            Fields = fields;
            Size = size;
            Alignment = alignment;
            Lifetime = AIBT.NodeMemoryLifetime.Activation;
        }
        public IReadOnlyList<ManifestField> Fields { get; }
        public uint Size { get; }
        public byte Alignment { get; }
        public AIBT.NodeMemoryLifetime Lifetime { get; }
    }

    public sealed class NodeManifest
    {
        public NodeManifest(ManifestLayout configuration, ManifestLayout memory)
        {
            Configuration = configuration;
            Memory = memory;
            TypeId = "aibt.verifier.node";
            Version = 1u;
            Kind = NodeBehaviorKind.Condition;
            Deterministic = true;
            Cancellation = NodeCancellationMode.NotApplicable;
            CostHint = NodeCostHint.Trivial;
            PossibleStatuses = new[] { AIBT.NodeStatus.Success, AIBT.NodeStatus.Failure };
        }
        public string TypeId { get; }
        public uint Version { get; }
        public NodeBehaviorKind Kind { get; }
        public bool Deterministic { get; }
        public NodeCancellationMode Cancellation { get; }
        public NodeCostHint CostHint { get; }
        public IReadOnlyList<AIBT.NodeStatus> PossibleStatuses { get; }
        public ManifestLayout Configuration { get; }
        public ManifestLayout Memory { get; }
    }

    public sealed class GeneratedNodeDescriptor
    {
        public GeneratedNodeDescriptor(
            IReadOnlyList<GeneratedStorageField> configuration,
            IReadOnlyList<GeneratedStorageField> memory)
            : this(SynthesizeManifest(configuration, memory), configuration, memory, Array.Empty<GeneratedBindingDescriptor>())
        {
        }

        public GeneratedNodeDescriptor(
            NodeManifest manifest,
            IReadOnlyList<GeneratedStorageField> configuration,
            IReadOnlyList<GeneratedStorageField> memory,
            IReadOnlyList<GeneratedBindingDescriptor> bindings)
            : this(manifest, configuration, memory, bindings, 0x0f, false)
        {
        }

        public GeneratedNodeDescriptor(
            NodeManifest manifest,
            IReadOnlyList<GeneratedStorageField> configuration,
            IReadOnlyList<GeneratedStorageField> memory,
            IReadOnlyList<GeneratedBindingDescriptor> bindings,
            byte callbackCapabilities,
            bool hasRandomStream)
        {
            Manifest = manifest;
            Configuration = configuration;
            Memory = memory;
            Bindings = bindings;
            CallbackCapabilities = callbackCapabilities;
            HasRandomStream = hasRandomStream;
            ConfigurationLayoutHash = Hash("c", configuration.Count);
            MemoryLayoutHash = Hash("m", memory.Count);
            AccessLayoutHash = Hash("a", bindings.Count);
        }

        public NodeManifest Manifest { get; }
        public IReadOnlyList<GeneratedStorageField> Configuration { get; }
        public IReadOnlyList<GeneratedStorageField> Memory { get; }
        public IReadOnlyList<GeneratedBindingDescriptor> Bindings { get; }
        public AIBT.CompiledHash ConfigurationLayoutHash { get; }
        public AIBT.CompiledHash MemoryLayoutHash { get; }
        public AIBT.CompiledHash AccessLayoutHash { get; }
        public byte CallbackCapabilities { get; }
        public bool HasRandomStream { get; }

        private static AIBT.CompiledHash Hash(string prefix, int value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(prefix + value.ToString(CultureInfo.InvariantCulture)));
            return new AIBT.CompiledHash(Convert.ToHexString(bytes).ToLowerInvariant());
        }

        private static NodeManifest SynthesizeManifest(
            IReadOnlyList<GeneratedStorageField> configuration,
            IReadOnlyList<GeneratedStorageField> memory)
            => new NodeManifest(Layout(configuration, true), Layout(memory, false));

        private static ManifestLayout Layout(IReadOnlyList<GeneratedStorageField> fields, bool configuration)
        {
            var manifest = new List<ManifestField>(fields.Count);
            uint size = 0;
            byte alignment = 1;
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                manifest.Add(new ManifestField(field.FieldId, field.Offset, field.Size, field.Alignment,
                    configuration && field.Encoding == GeneratedFieldEncoding.GeneratedHandle));
                size = Math.Max(size, checked(field.Offset + field.Size));
                alignment = Math.Max(alignment, field.Alignment);
            }
            if (alignment > 1)
            {
                var mask = (uint)alignment - 1u;
                size = checked((size + mask) & ~mask);
            }
            return new ManifestLayout(manifest, size, alignment);
        }
    }

    public sealed class RegisteredBlackboardTypeCatalogEntry
    {
        public RegisteredBlackboardTypeCatalogEntry(
            string canonicalTypeId,
            uint version,
            string schemaHash,
            AIBT.RegisteredUnmanagedTypeDescriptor descriptor,
            IReadOnlyList<GeneratedStorageField> fields,
            string? canonicalSchemaId = null)
        {
            CanonicalTypeId = canonicalTypeId;
            Version = version;
            SchemaHash = schemaHash;
            Descriptor = descriptor;
            Fields = fields;
            CanonicalSchemaId = canonicalSchemaId ?? canonicalTypeId + ".schema";
        }

        public string CanonicalTypeId { get; }
        public uint Version { get; }
        public string SchemaHash { get; }
        public string CanonicalSchemaId { get; }
        public AIBT.RegisteredUnmanagedTypeDescriptor Descriptor { get; }
        public IReadOnlyList<GeneratedStorageField> Fields { get; }
    }

    public sealed class RegisteredBlackboardTypeCatalog
    {
        private readonly IReadOnlyList<RegisteredBlackboardTypeCatalogEntry> entries;
        public RegisteredBlackboardTypeCatalog(IEnumerable<RegisteredBlackboardTypeCatalogEntry> entries)
            => this.entries = new List<RegisteredBlackboardTypeCatalogEntry>(entries);

        public IReadOnlyList<RegisteredBlackboardTypeCatalogEntry> Entries => entries;

        public bool TryGet(string canonicalTypeId, uint version, out RegisteredBlackboardTypeCatalogEntry entry)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var candidate = entries[index];
                if (candidate.CanonicalTypeId == canonicalTypeId && candidate.Version == version)
                {
                    entry = candidate;
                    return true;
                }
            }
            entry = null!;
            return false;
        }
    }

    public sealed class GeneratedShardMetadataArtifact
    {
        internal GeneratedShardMetadataArtifact(
            string shardId,
            uint shardVersion,
            IReadOnlyList<GeneratedNodeDescriptor> nodes,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            ShardId = shardId;
            ShardVersion = shardVersion;
            Nodes = nodes;
            RegisteredTypes = registeredTypes;
        }
        public string ShardId { get; }
        public uint ShardVersion { get; }
        public IReadOnlyList<GeneratedNodeDescriptor> Nodes { get; }
        public RegisteredBlackboardTypeCatalog RegisteredTypes { get; }
    }

    internal sealed class GeneratedByteWriter
    {
        private readonly MemoryStream stream = new MemoryStream();
        internal GeneratedByteWriter(string? domain)
        {
            if (domain != null) Raw(Encoding.UTF8.GetBytes(domain));
        }
        internal void U8(byte value) => stream.WriteByte(value);
        internal void U32(uint value) { U8((byte)value); U8((byte)(value >> 8)); U8((byte)(value >> 16)); U8((byte)(value >> 24)); }
        internal void U64(ulong value) { U32((uint)value); U32((uint)(value >> 32)); }
        internal void String(string value) { var bytes = Encoding.UTF8.GetBytes(value); U32((uint)bytes.Length); Raw(bytes); }
        internal void Hash(string value)
        {
            for (var index = 0; index < value.Length; index += 2)
                U8(byte.Parse(value.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        internal AIBT.CompiledHash Finish()
        {
            using var sha = SHA256.Create();
            return new AIBT.CompiledHash(Convert.ToHexString(sha.ComputeHash(stream.ToArray())).ToLowerInvariant());
        }
        private void Raw(byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
    }

    internal sealed class StubNodeRegistry
    {
        internal StubNodeRegistry(string hash) => Hash = hash;
        internal string Hash { get; }
    }

    internal sealed class StubNodeRegistryResult
    {
        internal StubNodeRegistryResult(string hash) { Success = true; Registry = new StubNodeRegistry(hash); }
        internal bool Success { get; }
        internal StubNodeRegistry Registry { get; }
    }

    public static class GeneratedNodeRegistry
    {
        internal static StubNodeRegistryResult Build(IEnumerable<GeneratedNodeDescriptor> nodes, bool includeBuiltIns = true)
        {
            using var sha = SHA256.Create();
            var text = string.Join(";", nodes);
            return new StubNodeRegistryResult(Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant());
        }
    }
}

namespace AIBT.Burst
{
    [Flags]
    public enum BurstNodeStatusMask : byte { None = 0, Success = 1, Failure = 2, Running = 4 }

    public readonly struct BurstHash256
    {
        public BurstHash256(uint a, uint b, uint c, uint d, uint e, uint f, uint g, uint h)
        { Word0 = a; Word1 = b; Word2 = c; Word3 = d; Word4 = e; Word5 = f; Word6 = g; Word7 = h; }
        public uint Word0 { get; } public uint Word1 { get; } public uint Word2 { get; } public uint Word3 { get; }
        public uint Word4 { get; } public uint Word5 { get; } public uint Word6 { get; } public uint Word7 { get; }
    }

    public readonly struct BurstCatalogFingerprint
    { public BurstCatalogFingerprint(BurstHash256 value) => Value = value; public BurstHash256 Value { get; } }

    public readonly struct BurstCatalogHandshake
    {
        public BurstCatalogHandshake(uint abiVersion, BurstCatalogFingerprint catalog, BurstHash256 nodeRegistry,
            uint compiledFormatVersion, uint executionSemanticsVersion, BurstHash256 configurationLayout,
            BurstHash256 memoryLayout, BurstHash256 accessLayout)
        { AbiVersion = abiVersion; Catalog = catalog; NodeRegistry = nodeRegistry; CompiledFormatVersion = compiledFormatVersion; ExecutionSemanticsVersion = executionSemanticsVersion; ConfigurationLayout = configurationLayout; MemoryLayout = memoryLayout; AccessLayout = accessLayout; }
        public uint AbiVersion { get; } public BurstCatalogFingerprint Catalog { get; } public BurstHash256 NodeRegistry { get; }
        public uint CompiledFormatVersion { get; } public uint ExecutionSemanticsVersion { get; }
        public BurstHash256 ConfigurationLayout { get; } public BurstHash256 MemoryLayout { get; } public BurstHash256 AccessLayout { get; }
    }
}

namespace AIBT.Execution.Burst.Dispatch
{
    [Flags]
    internal enum NativeBurstDispatchPhaseMaskV2 : byte
    { None = 0, Enter = 1, Tick = 2, Abort = 4, Exit = 8, Observer = 16 }

    internal readonly struct NativeBurstDispatchCaseV2
    {
        internal NativeBurstDispatchCaseV2(ulong typeNumericId, uint typeVersion, uint catalogCaseIndex,
            uint firstConfigurationField, uint configurationFieldCount, uint configurationSize,
            uint firstMemoryField, uint memoryFieldCount, uint memorySize, NativeBurstDispatchPhaseMaskV2 phases,
            AIBT.Burst.BurstNodeStatusMask possibleStatuses, bool hasRandomStream, uint firstBinding, uint bindingCount)
        { TypeNumericId = typeNumericId; TypeVersion = typeVersion; CatalogCaseIndex = catalogCaseIndex; FirstConfigurationField = firstConfigurationField; ConfigurationFieldCount = configurationFieldCount; ConfigurationSize = configurationSize; FirstMemoryField = firstMemoryField; MemoryFieldCount = memoryFieldCount; MemorySize = memorySize; Phases = phases; PossibleStatuses = possibleStatuses; HasRandomStream = hasRandomStream ? (byte)1 : (byte)0; FirstBinding = firstBinding; BindingCount = bindingCount; }
        internal ulong TypeNumericId { get; } internal uint TypeVersion { get; } internal uint CatalogCaseIndex { get; }
        internal uint FirstConfigurationField { get; } internal uint ConfigurationFieldCount { get; } internal uint ConfigurationSize { get; }
        internal uint FirstMemoryField { get; } internal uint MemoryFieldCount { get; } internal uint MemorySize { get; }
        internal NativeBurstDispatchPhaseMaskV2 Phases { get; } internal AIBT.Burst.BurstNodeStatusMask PossibleStatuses { get; }
        internal byte HasRandomStream { get; } internal uint FirstBinding { get; } internal uint BindingCount { get; }
    }
    internal enum NativeBurstDispatchBindingKindV2 : byte
    {
        BlackboardRead = 0, BlackboardWrite = 1, BlackboardReadWrite = 2,
        SnapshotRead = 3, EffectCommand = 4, AsyncOperation = 5, Completion = 6,
    }

    [Flags]
    internal enum NativeBurstDispatchBindingPhaseMaskV2 : byte
    {
        None = 0, Execute = 1, Cancel = 2, Completion = 4,
    }

    internal readonly struct NativeBurstDispatchBindingV2
    {
        internal const byte NoScope = 0xff;
        internal NativeBurstDispatchBindingV2(
            uint bindingOrdinal,
            uint configurationFieldOrdinal,
            NativeBurstDispatchBindingKindV2 kind,
            byte scope,
            NativeBurstDispatchBindingPhaseMaskV2 phaseMask,
            ulong primaryTypeNumericId,
            uint primaryTypeVersion,
            uint firstPrimaryValueField,
            uint primaryValueFieldCount,
            uint primaryValueSize,
            ulong secondaryTypeNumericId,
            uint secondaryTypeVersion,
            uint firstSecondaryValueField,
            uint secondaryValueFieldCount,
            uint secondaryValueSize)
        {
            BindingOrdinal = bindingOrdinal;
            ConfigurationFieldOrdinal = configurationFieldOrdinal;
            Kind = kind;
            Scope = scope;
            PhaseMask = phaseMask;
            PrimaryTypeNumericId = primaryTypeNumericId;
            PrimaryTypeVersion = primaryTypeVersion;
            FirstPrimaryValueField = firstPrimaryValueField;
            PrimaryValueFieldCount = primaryValueFieldCount;
            PrimaryValueSize = primaryValueSize;
            SecondaryTypeNumericId = secondaryTypeNumericId;
            SecondaryTypeVersion = secondaryTypeVersion;
            FirstSecondaryValueField = firstSecondaryValueField;
            SecondaryValueFieldCount = secondaryValueFieldCount;
            SecondaryValueSize = secondaryValueSize;
        }
        internal uint BindingOrdinal { get; }
        internal uint ConfigurationFieldOrdinal { get; }
        internal NativeBurstDispatchBindingKindV2 Kind { get; }
        internal byte Scope { get; }
        internal NativeBurstDispatchBindingPhaseMaskV2 PhaseMask { get; }
        internal ulong PrimaryTypeNumericId { get; }
        internal uint PrimaryTypeVersion { get; }
        internal uint FirstPrimaryValueField { get; }
        internal uint PrimaryValueFieldCount { get; }
        internal uint PrimaryValueSize { get; }
        internal ulong SecondaryTypeNumericId { get; }
        internal uint SecondaryTypeVersion { get; }
        internal uint FirstSecondaryValueField { get; }
        internal uint SecondaryValueFieldCount { get; }
        internal uint SecondaryValueSize { get; }
    }

    internal enum NativeBurstDispatchCanonicalRuleKindV2 : byte
    {
        None = 0, AgentId = 1, EntityId = 2, OperationId = 3, AssetId = 4,
        FixedString32 = 5, FixedString64 = 6, FixedString128 = 7, FixedString512 = 8,
    }

    internal readonly struct NativeBurstDispatchCanonicalRuleV2
    {
        internal NativeBurstDispatchCanonicalRuleV2(NativeBurstDispatchCanonicalRuleKindV2 kind, uint byteOffset)
        {
            Kind = kind;
            ByteOffset = byteOffset;
        }
        internal NativeBurstDispatchCanonicalRuleKindV2 Kind { get; }
        internal uint ByteOffset { get; }
    }

    internal readonly struct NativeBurstDispatchCanonicalRangeV2
    {
        internal NativeBurstDispatchCanonicalRangeV2(uint firstRule, uint ruleCount)
        {
            FirstRule = firstRule;
            RuleCount = ruleCount;
        }
        internal uint FirstRule { get; }
        internal uint RuleCount { get; }
    }

    internal enum NativeBurstDispatchFieldEncodingV2 : byte
    {
        Boolean = 0, Int8 = 1, UInt8 = 2, Int16 = 3, UInt16 = 4,
        Int32 = 5, UInt32 = 6, Int64 = 7, UInt64 = 8,
        Float32 = 9, Float64 = 10, GeneratedHandle = 11,
    }

    internal readonly struct NativeBurstDispatchFieldV2
    {
        internal NativeBurstDispatchFieldV2(
            uint fieldOrdinal,
            uint firstElementIndex,
            uint byteOffset,
            uint elementCount,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding)
            : this(fieldOrdinal, firstElementIndex, byteOffset, elementCount, elementSize, encoding,
                NativeBurstDispatchCanonicalRuleKindV2.None)
        {
        }

        internal NativeBurstDispatchFieldV2(
            uint fieldOrdinal,
            uint firstElementIndex,
            uint byteOffset,
            uint elementCount,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding,
            NativeBurstDispatchCanonicalRuleKindV2 canonicalRuleKind)
        {
            FieldOrdinal = fieldOrdinal;
            FirstElementIndex = firstElementIndex;
            ByteOffset = byteOffset;
            ElementCount = elementCount;
            ElementSize = elementSize;
            Encoding = encoding;
            CanonicalRuleKind = canonicalRuleKind;
        }

        internal uint FieldOrdinal { get; }
        internal uint FirstElementIndex { get; }
        internal uint ByteOffset { get; }
        internal uint ElementCount { get; }
        internal uint ElementSize { get; }
        internal NativeBurstDispatchFieldEncodingV2 Encoding { get; }
        internal NativeBurstDispatchCanonicalRuleKindV2 CanonicalRuleKind { get; }
    }
}
