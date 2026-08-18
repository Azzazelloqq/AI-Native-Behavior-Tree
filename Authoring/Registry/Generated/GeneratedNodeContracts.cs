using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
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

    public sealed class GeneratedTypeRecord
    {
        public GeneratedTypeRecord(
            GeneratedTypeRole role,
            string canonicalTypeId,
            uint version,
            string schemaHash = null,
            RegisteredUnmanagedTypeDescriptor registeredDescriptor = default)
        {
            if (!Enum.IsDefined(typeof(GeneratedTypeRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (string.IsNullOrEmpty(canonicalTypeId)) throw new ArgumentException("A canonical type ID is required.", nameof(canonicalTypeId));
            if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
            var registered = registeredDescriptor.IsValid;
            if (registered)
            {
                if (!NodeTypeIdRules.IsValid(canonicalTypeId)
                    || registeredDescriptor.TypeId != StableHash.Fnv1A64(canonicalTypeId)
                    || registeredDescriptor.Version != version
                    || registeredDescriptor.EqualityContractId != GeneratedNodeMetadata.CanonicalBytesEqualityContractId
                    || !registeredDescriptor.HasCanonicalSchema
                    || registeredDescriptor.HasMigration
                    || !IsHash(schemaHash))
                    throw new ArgumentException("Registered values require the accepted canonical codec/equality descriptor.", nameof(registeredDescriptor));
            }
            else if (!GeneratedTypeLayoutRules.TryBuiltIn(canonicalTypeId, out _, out _, out _)
                || version != 1)
            {
                throw new ArgumentException("Unknown values require an accepted registered descriptor.", nameof(registeredDescriptor));
            }
            else if (schemaHash != null && !IsZeroHash(schemaHash))
            {
                throw new ArgumentException("Built-in values use an all-zero schema hash.", nameof(schemaHash));
            }
            Role = role;
            CanonicalTypeId = canonicalTypeId;
            NumericTypeId = StableHash.Fnv1A64(canonicalTypeId);
            Version = version;
            SchemaHash = registered ? schemaHash : GeneratedNodeMetadata.ZeroHash;
            RegisteredDescriptor = registeredDescriptor;
        }

        public GeneratedTypeRole Role { get; }
        public string CanonicalTypeId { get; }
        public ulong NumericTypeId { get; }
        public uint Version { get; }
        public string SchemaHash { get; }
        public RegisteredUnmanagedTypeDescriptor RegisteredDescriptor { get; }

        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
                if ((value[index] < '0' || value[index] > '9') && (value[index] < 'a' || value[index] > 'f')) return false;
            return true;
        }

        private static bool IsZeroHash(string value) => IsHash(value) && value == GeneratedNodeMetadata.ZeroHash;
    }

    public sealed class GeneratedStorageField
    {
        public GeneratedStorageField(
            string fieldId,
            string valueTypeId,
            uint valueTypeVersion,
            uint size,
            byte alignment,
            GeneratedFieldEncoding encoding,
            string registeredSchemaHash = null,
            string bindingId = null,
            RegisteredUnmanagedTypeDescriptor registeredDescriptor = default)
        {
            if (!GeneratedIdentityRules.IsValidMemberId(fieldId)) throw new ArgumentException("Invalid canonical field ID.", nameof(fieldId));
            if (string.IsNullOrEmpty(valueTypeId)) throw new ArgumentException("A value type ID is required.", nameof(valueTypeId));
            if (valueTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(valueTypeVersion));
            if (size == 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (!ValidAlignment(alignment) || size % alignment != 0) throw new ArgumentOutOfRangeException(nameof(alignment));
            if (!Enum.IsDefined(typeof(GeneratedFieldEncoding), encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
            if ((encoding == GeneratedFieldEncoding.GeneratedHandle) != (bindingId != null))
                throw new ArgumentException("Generated handles require exactly one binding ID.", nameof(bindingId));
            if (bindingId != null && !GeneratedIdentityRules.IsValidMemberId(bindingId))
                throw new ArgumentException("Invalid canonical binding ID.", nameof(bindingId));
            if (encoding == GeneratedFieldEncoding.GeneratedHandle
                && (valueTypeId != "GeneratedHandle" || valueTypeVersion != 1 || size != 4 || alignment != 4))
                throw new ArgumentException("Generated handles have the fixed GeneratedHandle/v1/4/4 layout.");
            if (encoding == GeneratedFieldEncoding.GeneratedHandle
                && ((registeredSchemaHash != null && registeredSchemaHash != GeneratedNodeMetadata.ZeroHash)
                    || registeredDescriptor.IsValid))
                throw new ArgumentException("Generated handles cannot carry registered schema or equality metadata.");
            if (encoding == GeneratedFieldEncoding.Registered)
            {
                if (!NodeTypeIdRules.IsValid(valueTypeId)
                    || !registeredDescriptor.IsValid
                    || registeredDescriptor.TypeId != StableHash.Fnv1A64(valueTypeId)
                    || registeredDescriptor.Version != valueTypeVersion
                    || registeredDescriptor.Size != size
                    || registeredDescriptor.Alignment != alignment
                    || registeredDescriptor.EqualityContractId != GeneratedNodeMetadata.CanonicalBytesEqualityContractId
                    || !registeredDescriptor.HasCanonicalSchema
                    || registeredDescriptor.HasMigration
                    || !GeneratedTypeRecordHash.IsHash(registeredSchemaHash))
                    throw new ArgumentException("Registered storage requires the accepted schema/layout/equality descriptor.", nameof(registeredDescriptor));
            }
            else if (encoding != GeneratedFieldEncoding.GeneratedHandle)
            {
                if (!GeneratedTypeLayoutRules.TryBuiltIn(valueTypeId, out var expectedSize, out var expectedAlignment, out var expectedEncoding)
                    || valueTypeVersion != 1 || size != expectedSize || alignment != expectedAlignment || encoding != expectedEncoding
                    || (registeredSchemaHash != null && registeredSchemaHash != GeneratedNodeMetadata.ZeroHash)
                    || registeredDescriptor.IsValid)
                    throw new ArgumentException("Built-in storage type/version/encoding/layout must match the closed generated ABI.");
            }
            FieldId = fieldId;
            NumericFieldId = StableHash.Fnv1A64(fieldId);
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
        public uint Size { get; }
        public byte Alignment { get; }
        public GeneratedFieldEncoding Encoding { get; }
        public string RegisteredSchemaHash { get; }
        public string BindingId { get; }
        public RegisteredUnmanagedTypeDescriptor RegisteredDescriptor { get; }
        public uint Offset { get; internal set; }

        internal static bool ValidAlignment(byte value) => value == 1 || value == 2 || value == 4 || value == 8 || value == 16;
    }

    internal static class GeneratedTypeRecordHash
    {
        internal static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
                if ((value[index] < '0' || value[index] > '9') && (value[index] < 'a' || value[index] > 'f')) return false;
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

    public sealed class GeneratedBindingDescriptor
    {
        private readonly ReadOnlyCollection<GeneratedTypeRecord> _types;

        public GeneratedBindingDescriptor(
            string bindingId,
            GeneratedBindingKind kind,
            BlackboardScope scope,
            GeneratedPhaseCapability phaseCapabilities,
            IEnumerable<GeneratedTypeRecord> types)
        {
            if (!GeneratedIdentityRules.IsValidMemberId(bindingId)) throw new ArgumentException("Invalid canonical binding ID.", nameof(bindingId));
            if (!Enum.IsDefined(typeof(GeneratedBindingKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            var values = new List<GeneratedTypeRecord>(types ?? throw new ArgumentNullException(nameof(types)));
            values.Sort((left, right) => left.Role.CompareTo(right.Role));
            if (!ValidShape(kind, scope, phaseCapabilities, values)) throw new ArgumentException("Binding metadata does not match the closed ABI shape.");
            BindingId = bindingId;
            NumericBindingId = StableHash.Fnv1A64(bindingId);
            Kind = kind;
            Scope = scope;
            PhaseCapabilities = phaseCapabilities;
            _types = values.AsReadOnly();
        }

        public string BindingId { get; }
        public ulong NumericBindingId { get; }
        public GeneratedBindingKind Kind { get; }
        public BlackboardScope Scope { get; }
        public GeneratedPhaseCapability PhaseCapabilities { get; }
        public uint Ordinal { get; internal set; }
        public IReadOnlyList<GeneratedTypeRecord> Types => _types;
        public bool IsBlackboard => Kind <= GeneratedBindingKind.BlackboardReadWrite;

        private static bool ValidShape(GeneratedBindingKind kind, BlackboardScope scope, GeneratedPhaseCapability phase, IList<GeneratedTypeRecord> types)
        {
            if (kind <= GeneratedBindingKind.BlackboardReadWrite)
                return Enum.IsDefined(typeof(BlackboardScope), scope) && scope != BlackboardScope.NodeLocal
                    && !(scope == BlackboardScope.Shared && kind != GeneratedBindingKind.BlackboardRead)
                    && phase == GeneratedPhaseCapability.None && types.Count == 1 && types[0].Role == GeneratedTypeRole.Value;
            if ((byte)scope != byte.MaxValue) return false;
            if (kind == GeneratedBindingKind.SnapshotRead)
                return phase == GeneratedPhaseCapability.None && types.Count == 1 && types[0].Role == GeneratedTypeRole.Value;
            if (kind == GeneratedBindingKind.EffectCommand)
                return phase == GeneratedPhaseCapability.Execute && types.Count == 1 && types[0].Role == GeneratedTypeRole.EffectPayload;
            if (kind == GeneratedBindingKind.AsyncOperation)
                return phase == (GeneratedPhaseCapability.Execute | GeneratedPhaseCapability.Cancel) && types.Count == 2
                    && types[0].Role == GeneratedTypeRole.AsyncStartPayload && types[1].Role == GeneratedTypeRole.AsyncCancelPayload;
            return kind == GeneratedBindingKind.Completion && phase == GeneratedPhaseCapability.Completion
                && types.Count == 1 && types[0].Role == GeneratedTypeRole.CompletionPayload;
        }
    }

    public sealed class GeneratedNodeDescriptor
    {
        private readonly ReadOnlyCollection<GeneratedStorageField> _configuration;
        private readonly ReadOnlyCollection<GeneratedStorageField> _memory;
        private readonly ReadOnlyCollection<GeneratedBindingDescriptor> _bindings;

        public GeneratedNodeDescriptor(
            NodeManifest manifest,
            IEnumerable<GeneratedStorageField> configuration,
            IEnumerable<GeneratedStorageField> memory,
            IEnumerable<GeneratedBindingDescriptor> bindings)
            : this(manifest, configuration, memory, bindings, 0x0f, false)
        {
        }

        public GeneratedNodeDescriptor(
            NodeManifest manifest,
            IEnumerable<GeneratedStorageField> configuration,
            IEnumerable<GeneratedStorageField> memory,
            IEnumerable<GeneratedBindingDescriptor> bindings,
            byte callbackCapabilities,
            bool hasRandomStream)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            if (callbackCapabilities != 0x0f && callbackCapabilities != 0x1f)
                throw new ArgumentOutOfRangeException(nameof(callbackCapabilities));
            _configuration = GeneratedNodeMetadata.Layout(configuration, out var configSize, out var configAlignment);
            _memory = GeneratedNodeMetadata.Layout(memory, out var memorySize, out var memoryAlignment);
            _bindings = GeneratedNodeMetadata.Bindings(bindings);
            if (manifest.Configuration.Size != configSize || manifest.Configuration.Alignment != configAlignment
                || manifest.Memory.Size != memorySize || manifest.Memory.Alignment != memoryAlignment)
                throw new ArgumentException("Generated layouts must match the P1 manifest projection.");
            for (var index = 0; index < manifest.Configuration.Fields.Count; index++)
            {
                var manifestField = manifest.Configuration.Fields[index];
                GeneratedStorageField generatedField = null;
                for (var generatedIndex = 0; generatedIndex < _configuration.Count; generatedIndex++)
                    if (_configuration[generatedIndex].FieldId == manifestField.ParameterName) { generatedField = _configuration[generatedIndex]; break; }
                if (generatedField == null
                    || manifestField.IsGeneratedHandle != (generatedField.Encoding == GeneratedFieldEncoding.GeneratedHandle)
                    || manifestField.Offset != generatedField.Offset
                    || manifestField.Size != generatedField.Size
                    || manifestField.Alignment != generatedField.Alignment)
                    throw new ArgumentException("Generated configuration fields must match the manifest packing projection.");
            }
            GeneratedNodeMetadata.ValidateFieldBindings(_configuration, _bindings);
            ConfigurationLayoutHash = GeneratedNodeMetadata.HashLayout(this, true);
            MemoryLayoutHash = GeneratedNodeMetadata.HashLayout(this, false);
            AccessLayoutHash = GeneratedNodeMetadata.HashAccess(this);
            CallbackCapabilities = callbackCapabilities;
            HasRandomStream = hasRandomStream;
        }

        public NodeManifest Manifest { get; }
        public IReadOnlyList<GeneratedStorageField> Configuration => _configuration;
        public IReadOnlyList<GeneratedStorageField> Memory => _memory;
        public IReadOnlyList<GeneratedBindingDescriptor> Bindings => _bindings;
        public CompiledHash ConfigurationLayoutHash { get; }
        public CompiledHash MemoryLayoutHash { get; }
        public CompiledHash AccessLayoutHash { get; }
        public byte CallbackCapabilities { get; }
        public bool HasRandomStream { get; }
    }
}
