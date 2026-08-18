using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Collections;

namespace AIBT
{
    public enum NativeBlackboardAccessModeV2 : byte
    {
        Read = 1,
        Write = 2,
        ReadWrite = 3,
    }

    public enum NativeBlackboardReductionKindV2 : byte
    {
        None = 0, Min = 1, Max = 2, Sum = 3,
        Any = 4, All = 5, First = 6, Last = 7,
    }

    public enum NativeBlackboardFieldEncodingV2 : byte
    {
        Bool8 = 0, Int8 = 1, UInt8 = 2, Int16LE = 3, UInt16LE = 4,
        Int32LE = 5, UInt32LE = 6, Int64LE = 7, UInt64LE = 8,
        Float32BitsLE = 9, Float64BitsLE = 10, FixedBytes = 11,
        GeneratedHandle = 12, Registered = 13,
    }

    public sealed class NativeBlackboardScopeBindingV2
    {
        private readonly byte[] _schema;
        private readonly byte[] _rawLayout;

        public NativeBlackboardScopeBindingV2(
            BlackboardScope scope,
            string contractId,
            uint contractVersion,
            uint firstSlot,
            uint slotCount,
            byte[] schema,
            byte[] rawLayout)
        {
            if (scope != BlackboardScope.Agent && scope != BlackboardScope.Shared) throw new ArgumentOutOfRangeException(nameof(scope));
            if (string.IsNullOrEmpty(contractId)) throw new ArgumentException("A canonical scope contract ID is required.", nameof(contractId));
            if (contractVersion == 0) throw new ArgumentOutOfRangeException(nameof(contractVersion));
            _schema = (byte[])(schema ?? throw new ArgumentNullException(nameof(schema))).Clone();
            _rawLayout = (byte[])(rawLayout ?? throw new ArgumentNullException(nameof(rawLayout))).Clone();
            if (_schema.Length == 0 || _rawLayout.Length == 0) throw new ArgumentException("Canonical scope preimages are required.");
            Scope = scope;
            ContractId = contractId;
            ContractNumericId = StableHash.Fnv1A64(contractId);
            if (ContractNumericId == 0) throw new ArgumentException("The scope contract ID hashes to zero.", nameof(contractId));
            ContractVersion = contractVersion;
            SchemaHash = new CompiledHash(StableHash.Sha256Hex(_schema));
            LayoutHash = new CompiledHash(StableHash.Sha256Hex(_rawLayout));
            FirstSlot = firstSlot;
            SlotCount = slotCount;
        }

        public BlackboardScope Scope { get; }
        public string ContractId { get; }
        public ulong ContractNumericId { get; }
        public uint ContractVersion { get; }
        public CompiledHash SchemaHash { get; }
        public CompiledHash LayoutHash { get; }
        public uint FirstSlot { get; }
        public uint SlotCount { get; }
        public byte[] GetSchemaBytesCopy() => (byte[])_schema.Clone();
        public byte[] GetRawLayoutCopy() => (byte[])_rawLayout.Clone();
    }

    public readonly struct NativeBlackboardSlotBindingV2
    {
        public NativeBlackboardSlotBindingV2(
            ulong stableKeyId, ulong typeId, uint typeVersion, ulong enumContractId,
            BlackboardScope scope, uint scopeSlotIndex, uint offset, uint size, uint alignment,
            uint defaultOffset, uint defaultSize, CompiledBlackboardAccessFlags accessFlags,
            uint scopeDescriptorIndex, uint registeredTypeIndex, NativeBlackboardReductionKindV2 reduction)
        {
            StableKeyId = stableKeyId; TypeId = typeId; TypeVersion = typeVersion; EnumContractId = enumContractId;
            Scope = scope; ScopeSlotIndex = scopeSlotIndex; Offset = offset; Size = size; Alignment = alignment;
            DefaultOffset = defaultOffset; DefaultSize = defaultSize; AccessFlags = accessFlags;
            ScopeDescriptorIndex = scopeDescriptorIndex; RegisteredTypeIndex = registeredTypeIndex;
            Reduction = reduction;
        }
        public ulong StableKeyId { get; }
        public ulong TypeId { get; }
        public uint TypeVersion { get; }
        public ulong EnumContractId { get; }
        public BlackboardScope Scope { get; }
        public uint ScopeSlotIndex { get; }
        public uint Offset { get; }
        public uint Size { get; }
        public uint Alignment { get; }
        public uint DefaultOffset { get; }
        public uint DefaultSize { get; }
        public CompiledBlackboardAccessFlags AccessFlags { get; }
        public uint ScopeDescriptorIndex { get; }
        public uint RegisteredTypeIndex { get; }
        public NativeBlackboardReductionKindV2 Reduction { get; }
    }

    public sealed class NativeBlackboardSlotAuthorityV2
    {
        private readonly byte[] _canonicalDefaultJson;
        public NativeBlackboardSlotAuthorityV2(
            string canonicalKeyId,
            string canonicalTypeId,
            string enumContract,
            byte[] canonicalDefaultJson)
        {
            if (string.IsNullOrEmpty(canonicalKeyId) || string.IsNullOrEmpty(canonicalTypeId))
                throw new ArgumentException("Canonical key and type IDs are required.");
            CanonicalKeyId = canonicalKeyId;
            CanonicalTypeId = canonicalTypeId;
            EnumContract = enumContract ?? string.Empty;
            _canonicalDefaultJson = (byte[])(canonicalDefaultJson ?? throw new ArgumentNullException(nameof(canonicalDefaultJson))).Clone();
        }
        public string CanonicalKeyId { get; }
        public string CanonicalTypeId { get; }
        public string EnumContract { get; }
        public byte[] GetCanonicalDefaultJsonCopy() => (byte[])_canonicalDefaultJson.Clone();

        public static NativeBlackboardSlotAuthorityV2 CreateBuiltIn(
            string canonicalKeyId,
            BlackboardTypeDescriptor descriptor,
            string enumContract,
            byte[] compiledDefault)
        {
            if (descriptor.ValueType == BlackboardValueType.Registered) throw new ArgumentOutOfRangeException(nameof(descriptor));
            return new NativeBlackboardSlotAuthorityV2(
                canonicalKeyId, descriptor.ValueType.ToString(), enumContract,
                NativeCompiledProgramV2Verifier.CanonicalDefaultJson(
                    descriptor.ValueType, enumContract ?? string.Empty, compiledDefault, 0, (uint)compiledDefault.Length));
        }
    }

    public readonly struct NativeBlackboardAccessBindingV2
    {
        public NativeBlackboardAccessBindingV2(
            uint nodeIndex, uint accessOrdinal, BlackboardScope scope, uint slotIndex,
            NativeBlackboardAccessModeV2 mode, ulong typeId, uint typeVersion,
            ulong enumContractId, uint registeredTypeIndex, NativeBlackboardReductionKindV2 reduction)
        {
            NodeIndex = nodeIndex; AccessOrdinal = accessOrdinal; Scope = scope; SlotIndex = slotIndex;
            Mode = mode; TypeId = typeId; TypeVersion = typeVersion; EnumContractId = enumContractId;
            RegisteredTypeIndex = registeredTypeIndex;
            Reduction = reduction;
        }
        public uint NodeIndex { get; }
        public uint AccessOrdinal { get; }
        public BlackboardScope Scope { get; }
        public uint SlotIndex { get; }
        public NativeBlackboardAccessModeV2 Mode { get; }
        public ulong TypeId { get; }
        public uint TypeVersion { get; }
        public ulong EnumContractId { get; }
        public uint RegisteredTypeIndex { get; }
        public NativeBlackboardReductionKindV2 Reduction { get; }
    }

    public readonly struct NativeBlackboardWatchedSlotBindingV2
    {
        public NativeBlackboardWatchedSlotBindingV2(BlackboardScope scope, uint slotIndex)
        { Scope = scope; SlotIndex = slotIndex; }
        public BlackboardScope Scope { get; }
        public uint SlotIndex { get; }
    }

    public readonly struct NativeBlackboardAccessRecordV2
    {
        internal NativeBlackboardAccessRecordV2(NativeBlackboardAccessBindingV2 value, uint resolvedSlotIndex)
        {
            NodeIndex = value.NodeIndex; AccessOrdinal = value.AccessOrdinal; Scope = value.Scope;
            ScopeSlotIndex = value.SlotIndex; ResolvedSlotIndex = resolvedSlotIndex; Mode = value.Mode;
            TypeId = value.TypeId; TypeVersion = value.TypeVersion; EnumContractId = value.EnumContractId;
            RegisteredTypeIndex = value.RegisteredTypeIndex; Reduction = value.Reduction;
        }
        public uint NodeIndex { get; }
        public uint AccessOrdinal { get; }
        public BlackboardScope Scope { get; }
        public uint ScopeSlotIndex { get; }
        public uint ResolvedSlotIndex { get; }
        public NativeBlackboardAccessModeV2 Mode { get; }
        public ulong TypeId { get; }
        public uint TypeVersion { get; }
        public ulong EnumContractId { get; }
        public uint RegisteredTypeIndex { get; }
        public NativeBlackboardReductionKindV2 Reduction { get; }
    }

    public readonly struct NativeRegisteredBlackboardTypeBindingV2
    {
        private readonly byte[] _schemaPreimage;

        public NativeRegisteredBlackboardTypeBindingV2(
            RegisteredUnmanagedTypeDescriptor descriptor,
            byte[] schemaPreimage,
            uint firstField,
            uint fieldCount)
        {
            if (!descriptor.IsValid || !descriptor.HasCanonicalSchema)
                throw new ArgumentException("An exact registered type/schema descriptor is required.");
            _schemaPreimage = (byte[])(schemaPreimage ?? throw new ArgumentNullException(nameof(schemaPreimage))).Clone();
            if (_schemaPreimage.Length == 0) throw new ArgumentException("The exact registered schema preimage is required.", nameof(schemaPreimage));
            Descriptor = descriptor;
            SchemaHash = new CompiledHash(StableHash.Sha256Hex(_schemaPreimage));
            FirstField = firstField;
            FieldCount = fieldCount;
        }

        public NativeRegisteredBlackboardTypeBindingV2(
            RegisteredUnmanagedTypeDescriptor descriptor,
            CompiledHash schemaHash,
            uint firstField,
            uint fieldCount)
        {
            if (!descriptor.IsValid || !descriptor.HasCanonicalSchema || !schemaHash.IsValid)
                throw new ArgumentException("An exact registered type/schema descriptor is required.");
            Descriptor = descriptor; SchemaHash = schemaHash; FirstField = firstField; FieldCount = fieldCount;
            _schemaPreimage = Array.Empty<byte>();
        }
        public RegisteredUnmanagedTypeDescriptor Descriptor { get; }
        public CompiledHash SchemaHash { get; }
        public uint FirstField { get; }
        public uint FieldCount { get; }
        public byte[] GetSchemaPreimageCopy() => _schemaPreimage == null ? Array.Empty<byte>() : (byte[])_schemaPreimage.Clone();
    }

    public readonly struct NativeRegisteredBlackboardFieldBindingV2
    {
        public NativeRegisteredBlackboardFieldBindingV2(
            ulong fieldId, ulong valueTypeId, uint valueTypeVersion, uint offset,
            uint size, uint alignment, NativeBlackboardFieldEncodingV2 encoding,
            ulong registeredSchemaId, CompiledHash registeredSchemaHash, ulong equalityContractId)
        {
            FieldId = fieldId; ValueTypeId = valueTypeId; ValueTypeVersion = valueTypeVersion; Offset = offset;
            Size = size; Alignment = alignment; Encoding = encoding; RegisteredSchemaId = registeredSchemaId;
            RegisteredSchemaHash = registeredSchemaHash; EqualityContractId = equalityContractId;
        }
        public ulong FieldId { get; }
        public ulong ValueTypeId { get; }
        public uint ValueTypeVersion { get; }
        public uint Offset { get; }
        public uint Size { get; }
        public uint Alignment { get; }
        public NativeBlackboardFieldEncodingV2 Encoding { get; }
        public ulong RegisteredSchemaId { get; }
        public CompiledHash RegisteredSchemaHash { get; }
        public ulong EqualityContractId { get; }
    }

    public sealed class NativeProgramBlackboardBindingV2
    {
        private readonly byte[] _outerPreimage;
        private readonly ReadOnlyCollection<NativeBlackboardScopeBindingV2> _scopes;
        private readonly ReadOnlyCollection<NativeBlackboardSlotBindingV2> _slots;
        private readonly ReadOnlyCollection<NativeBlackboardAccessBindingV2> _accesses;
        private readonly ReadOnlyCollection<NativeBlackboardSlotAuthorityV2> _slotAuthorities;
        private readonly ReadOnlyCollection<NativeRegisteredBlackboardTypeBindingV2> _registeredTypes;
        private readonly ReadOnlyCollection<NativeRegisteredBlackboardFieldBindingV2> _registeredFields;
        private readonly ReadOnlyCollection<NativeBlackboardWatchedSlotBindingV2> _watchedSlots;

        public NativeProgramBlackboardBindingV2(
            CompiledProgram semanticProgram,
            byte[] outerPreimage,
            IEnumerable<NativeBlackboardScopeBindingV2> scopes,
            IEnumerable<NativeBlackboardSlotBindingV2> slots,
            IEnumerable<NativeBlackboardSlotAuthorityV2> slotAuthorities,
            IEnumerable<NativeBlackboardAccessBindingV2> accesses,
            IEnumerable<NativeBlackboardWatchedSlotBindingV2> watchedSlots,
            IEnumerable<NativeRegisteredBlackboardTypeBindingV2> registeredTypes,
            IEnumerable<NativeRegisteredBlackboardFieldBindingV2> registeredFields)
        {
            SemanticProgram = semanticProgram ?? throw new ArgumentNullException(nameof(semanticProgram));
            _outerPreimage = (byte[])(outerPreimage ?? throw new ArgumentNullException(nameof(outerPreimage))).Clone();
            if (_outerPreimage.Length == 0) throw new ArgumentException("The exact compiled-v2 preimage is required.", nameof(outerPreimage));
            OuterContentHash = new CompiledHash(StableHash.Sha256Hex(_outerPreimage));
            _scopes = Copy(scopes, nameof(scopes));
            _slots = Copy(slots, nameof(slots));
            _slotAuthorities = Copy(slotAuthorities, nameof(slotAuthorities));
            _accesses = Copy(accesses, nameof(accesses));
            _watchedSlots = Copy(watchedSlots, nameof(watchedSlots));
            _registeredTypes = Copy(registeredTypes, nameof(registeredTypes));
            _registeredFields = Copy(registeredFields, nameof(registeredFields));
        }

        public CompiledProgram SemanticProgram { get; }
        public CompiledHash OuterContentHash { get; }
        public IReadOnlyList<NativeBlackboardScopeBindingV2> Scopes => _scopes;
        public IReadOnlyList<NativeBlackboardSlotBindingV2> Slots => _slots;
        public IReadOnlyList<NativeBlackboardSlotAuthorityV2> SlotAuthorities => _slotAuthorities;
        public IReadOnlyList<NativeBlackboardAccessBindingV2> Accesses => _accesses;
        public IReadOnlyList<NativeBlackboardWatchedSlotBindingV2> WatchedSlots => _watchedSlots;
        public IReadOnlyList<NativeRegisteredBlackboardTypeBindingV2> RegisteredTypes => _registeredTypes;
        public IReadOnlyList<NativeRegisteredBlackboardFieldBindingV2> RegisteredFields => _registeredFields;
        public byte[] GetOuterPreimageCopy() => (byte[])_outerPreimage.Clone();

        internal NativeCompiledProgramHeaderV1 CreateHeaderProjection()
        {
            var value = SemanticProgram.Header;
            var header = new CompiledProgramHeader(
                2, value.ExecutionSemanticsVersion, value.CompilerVersion,
                value.CanonicalSemanticHash, value.NodeRegistryHash, value.CanonicalPolicyHash,
                value.PolicyFormatVersion, OuterContentHash, value.RootNodeIndex,
                value.NodeCount, value.ChildIndexCount, (uint)_slots.Count, value.DebugMapCount,
                value.ConfigBlobSize, value.InstanceNodeMemorySize, value.RequiredMaximumAlignment,
                value.CapabilityFlags, value.DeterministicModeCompatible);
            return new NativeCompiledProgramHeaderV1(header);
        }

        private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string name)
            => new List<T>(values ?? throw new ArgumentNullException(name)).AsReadOnly();
    }

    public readonly struct NativeProgramImageCapacityV2
    {
        public NativeProgramImageCapacityV2(
            NativeProgramImageCapacityV1 semantic, uint scopeDescriptors, uint scopeLayoutBytes,
            uint slots, uint accesses, uint nodeAccessRanges, uint registeredTypes, uint registeredFields)
        {
            Semantic = semantic; ScopeDescriptors = scopeDescriptors; ScopeLayoutBytes = scopeLayoutBytes;
            Slots = slots; Accesses = accesses; NodeAccessRanges = nodeAccessRanges;
            RegisteredTypes = registeredTypes; RegisteredFields = registeredFields;
        }
        public NativeProgramImageCapacityV1 Semantic { get; }
        public uint ScopeDescriptors { get; }
        public uint ScopeLayoutBytes { get; }
        public uint Slots { get; }
        public uint Accesses { get; }
        public uint NodeAccessRanges { get; }
        public uint RegisteredTypes { get; }
        public uint RegisteredFields { get; }

        public static NativeProgramImageCapacityV2 Exact(NativeProgramBlackboardBindingV2 binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            ulong layoutBytes = 0;
            for (var index = 0; index < binding.Scopes.Count; index++) layoutBytes += (uint)binding.Scopes[index].GetRawLayoutCopy().Length;
            if (layoutBytes > uint.MaxValue) throw new OverflowException("Scope layouts exceed UInt32.");
            return new NativeProgramImageCapacityV2(
                NativeProgramImageCapacityV1.Exact(binding.SemanticProgram),
                (uint)binding.Scopes.Count, (uint)layoutBytes, (uint)binding.Slots.Count,
                (uint)binding.Accesses.Count, (uint)binding.SemanticProgram.Nodes.Count,
                (uint)binding.RegisteredTypes.Count, (uint)binding.RegisteredFields.Count);
        }
    }

    public readonly struct NativeBlackboardScopeRecordV2
    {
        internal NativeBlackboardScopeRecordV2(NativeBlackboardScopeBindingV2 value, uint rawOffset, uint rawLength)
        { Scope = value.Scope; ContractId = value.ContractNumericId; ContractVersion = value.ContractVersion; SchemaHash = new NativeHash256V1(value.SchemaHash); LayoutHash = new NativeHash256V1(value.LayoutHash); FirstSlot = value.FirstSlot; SlotCount = value.SlotCount; RawLayoutOffset = rawOffset; RawLayoutLength = rawLength; }
        public BlackboardScope Scope { get; }
        public ulong ContractId { get; }
        public uint ContractVersion { get; }
        public NativeHash256V1 SchemaHash { get; }
        public NativeHash256V1 LayoutHash { get; }
        public uint FirstSlot { get; }
        public uint SlotCount { get; }
        public uint RawLayoutOffset { get; }
        public uint RawLayoutLength { get; }
    }

    public readonly struct NativeNodeBlackboardAccessRangeV2
    {
        internal NativeNodeBlackboardAccessRangeV2(uint offset, uint count) { Offset = offset; Count = count; }
        public uint Offset { get; }
        public uint Count { get; }
    }

    public readonly struct NativeRegisteredBlackboardTypeRecordV2
    {
        internal NativeRegisteredBlackboardTypeRecordV2(NativeRegisteredBlackboardTypeBindingV2 value)
        { Descriptor = value.Descriptor; SchemaHash = new NativeHash256V1(value.SchemaHash); FirstField = value.FirstField; FieldCount = value.FieldCount; }
        public RegisteredUnmanagedTypeDescriptor Descriptor { get; }
        public NativeHash256V1 SchemaHash { get; }
        public uint FirstField { get; }
        public uint FieldCount { get; }
    }

    public readonly struct NativeRegisteredBlackboardFieldRecordV2
    {
        internal NativeRegisteredBlackboardFieldRecordV2(NativeRegisteredBlackboardFieldBindingV2 value)
        { FieldId = value.FieldId; ValueTypeId = value.ValueTypeId; ValueTypeVersion = value.ValueTypeVersion; Offset = value.Offset; Size = value.Size; Alignment = value.Alignment; Encoding = value.Encoding; RegisteredSchemaId = value.RegisteredSchemaId; RegisteredSchemaHash = value.RegisteredSchemaHash.IsValid ? new NativeHash256V1(value.RegisteredSchemaHash) : default; EqualityContractId = value.EqualityContractId; }
        public ulong FieldId { get; }
        public ulong ValueTypeId { get; }
        public uint ValueTypeVersion { get; }
        public uint Offset { get; }
        public uint Size { get; }
        public uint Alignment { get; }
        public NativeBlackboardFieldEncodingV2 Encoding { get; }
        public ulong RegisteredSchemaId { get; }
        public NativeHash256V1 RegisteredSchemaHash { get; }
        public ulong EqualityContractId { get; }
    }

    public readonly struct NativeProgramImageViewV2
    {
        internal NativeProgramImageViewV2(
            NativeProgramImageViewV1 semantic, NativeCompiledProgramHeaderV1 header,
            NativeArray<NativeBlackboardScopeRecordV2>.ReadOnly scopes, NativeArray<byte>.ReadOnly scopeLayoutBytes,
            NativeArray<NativeBlackboardSlotBindingV2>.ReadOnly slots,
            NativeArray<NativeBlackboardAccessRecordV2>.ReadOnly accesses,
            NativeArray<NativeNodeBlackboardAccessRangeV2>.ReadOnly nodeAccessRanges,
            NativeArray<NativeRegisteredBlackboardTypeRecordV2>.ReadOnly registeredTypes,
            NativeArray<NativeRegisteredBlackboardFieldRecordV2>.ReadOnly registeredFields)
        { Semantic = semantic; Header = header; Scopes = scopes; ScopeLayoutBytes = scopeLayoutBytes; Slots = slots; Accesses = accesses; NodeAccessRanges = nodeAccessRanges; RegisteredTypes = registeredTypes; RegisteredFields = registeredFields; }
        public NativeProgramImageViewV1 Semantic { get; }
        public NativeCompiledProgramHeaderV1 Header { get; }
        public NativeArray<NativeBlackboardScopeRecordV2>.ReadOnly Scopes { get; }
        public NativeArray<byte>.ReadOnly ScopeLayoutBytes { get; }
        public NativeArray<NativeBlackboardSlotBindingV2>.ReadOnly Slots { get; }
        public NativeArray<NativeBlackboardAccessRecordV2>.ReadOnly Accesses { get; }
        public NativeArray<NativeNodeBlackboardAccessRangeV2>.ReadOnly NodeAccessRanges { get; }
        public NativeArray<NativeRegisteredBlackboardTypeRecordV2>.ReadOnly RegisteredTypes { get; }
        public NativeArray<NativeRegisteredBlackboardFieldRecordV2>.ReadOnly RegisteredFields { get; }
    }

    public readonly struct NativeProgramReadLeaseV2
    {
        internal NativeProgramReadLeaseV2(NativeProgramReadLeaseV1 semanticLease, NativeProgramImageViewV2 view)
        { SemanticLease = semanticLease; View = view; }
        internal NativeProgramReadLeaseV1 SemanticLease { get; }
        public NativeLeaseTokenV1 Token => SemanticLease.Token;
        public NativeProgramImageViewV2 View { get; }
    }
}
