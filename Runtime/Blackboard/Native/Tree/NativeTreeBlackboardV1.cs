using System;
using AIBT.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT
{
    internal static class NativeBuiltInBlackboardTypeIdsV1
    {
        internal const ulong Bool = 17851659414560344221UL;
        internal const ulong Int32 = 13376016304518341055UL;
        internal const ulong Int64 = 13371240026006338596UL;
        internal const ulong Float32 = 262399420830678928UL;
        internal const ulong Float64 = 259465923807179655UL;
        internal const ulong Float2 = 10476436347436866485UL;
        internal const ulong Float3 = 10476435247925238274UL;
        internal const ulong Quaternion = 17419964233870898637UL;
        internal const ulong Enum32 = 10202037613171143745UL;
        internal const ulong FixedString32 = 3747676040207735061UL;
        internal const ulong FixedString64 = 3742886567556194070UL;
        internal const ulong FixedString128 = 2407326393709615197UL;
        internal const ulong FixedString512 = 18358319809526921596UL;
        internal const ulong AgentId = 3037422141412130939UL;
        internal const ulong EntityId = 15061901384457708023UL;
        internal const ulong OperationId = 12623251205181426651UL;
        internal const ulong AssetId = 7042187404466474134UL;
    }

    public readonly struct NativeBlackboardTypeIdV2 : IEquatable<NativeBlackboardTypeIdV2>
    {
        private NativeBlackboardTypeIdV2(
            ulong typeId, uint version, uint size, uint alignment, ulong enumContractId,
            uint registeredTypeIndex, ulong schemaId, NativeHash256V1 schemaHash, ulong equalityContractId)
        {
            TypeId = typeId; Version = version; Size = size; Alignment = alignment;
            EnumContractId = enumContractId; RegisteredTypeIndex = registeredTypeIndex;
            SchemaId = schemaId; SchemaHash = schemaHash; EqualityContractId = equalityContractId;
        }

        public ulong TypeId { get; }
        public uint Version { get; }
        public uint Size { get; }
        public uint Alignment { get; }
        public ulong EnumContractId { get; }
        public uint RegisteredTypeIndex { get; }
        public ulong SchemaId { get; }
        public NativeHash256V1 SchemaHash { get; }
        public ulong EqualityContractId { get; }
        public bool IsRegistered => RegisteredTypeIndex != CompiledIndex.Invalid;

        public static NativeBlackboardTypeIdV2 BuiltIn(BlackboardTypeDescriptor descriptor, ulong enumContractId = 0)
        {
            if (!descriptor.IsValid || descriptor.ValueType == BlackboardValueType.Registered)
                throw new ArgumentException("A built-in blackboard descriptor is required.", nameof(descriptor));
            if ((descriptor.ValueType == BlackboardValueType.Enum32) != (enumContractId != 0))
                throw new ArgumentException("Enum32 requires exactly one nonzero enum contract.", nameof(enumContractId));
            return new NativeBlackboardTypeIdV2(
                descriptor.TypeId, descriptor.Version, (uint)descriptor.Size, (uint)descriptor.Alignment,
                enumContractId, CompiledIndex.Invalid, 0, default, 0);
        }

        public static NativeBlackboardTypeIdV2 Registered(
            uint registeredTypeIndex,
            NativeRegisteredBlackboardTypeRecordV2 record)
        {
            if (registeredTypeIndex == CompiledIndex.Invalid || !record.Descriptor.IsValid)
                throw new ArgumentException("A registered native type record is required.", nameof(record));
            var value = record.Descriptor;
            return new NativeBlackboardTypeIdV2(
                value.TypeId, value.Version, (uint)value.Size, (uint)value.Alignment, 0,
                registeredTypeIndex, value.CanonicalSchemaId, record.SchemaHash, value.EqualityContractId);
        }

        public bool Equals(NativeBlackboardTypeIdV2 other)
            => TypeId == other.TypeId && Version == other.Version && Size == other.Size && Alignment == other.Alignment
                && EnumContractId == other.EnumContractId && RegisteredTypeIndex == other.RegisteredTypeIndex
                && SchemaId == other.SchemaId && SchemaHash == other.SchemaHash && EqualityContractId == other.EqualityContractId;
        public override bool Equals(object obj) => obj is NativeBlackboardTypeIdV2 other && Equals(other);
        public override int GetHashCode() => TypeId.GetHashCode() ^ (int)Version ^ (int)Size ^ (int)RegisteredTypeIndex;
    }

    public static class NativeTreeBlackboardV1
    {
        public static BurstContextResult TryRead<T>(
            NativeProgramImageViewV2 program,
            NativeInstanceArenaViewV2 instance,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            out T value,
            out ulong version)
            where T : unmanaged
        {
            value = default;
            version = 0;
            var validation = TryResolve(program, nodeIndex, accessOrdinal, expectedType, BlackboardScope.Tree, false, out var access, out var slot);
            if (validation != BurstContextResult.Success) return validation;
            if (UnsafeUtility.SizeOf<T>() != slot.Size || UnsafeUtility.AlignOf<T>() != slot.Alignment)
                return BurstContextResult.TypeMismatch;
            if ((ulong)slot.Offset + slot.Size > (uint)instance.Semantic.TreeBlackboard.Length
                || access.ResolvedSlotIndex >= instance.TreeSlotVersions.Length)
                return BurstContextResult.InvalidHandle;
            var slice = new NativeSlice<byte>(instance.Semantic.TreeBlackboard, (int)slot.Offset, (int)slot.Size);
            value = slice.SliceConvert<T>()[0];
            version = instance.TreeSlotVersions[(int)access.ResolvedSlotIndex];
            return BurstContextResult.Success;
        }

        public static BurstContextResult TryWrite<T>(
            NativeProgramImageViewV2 program,
            NativeInstanceArenaViewV2 instance,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            NativeArray<T> candidate,
            out bool changed)
            where T : unmanaged
        {
            changed = false;
            var validation = TryResolve(program, nodeIndex, accessOrdinal, expectedType, BlackboardScope.Tree, true, out var access, out var slot);
            if (validation != BurstContextResult.Success) return validation;
            if (!candidate.IsCreated || candidate.Length != 1 || UnsafeUtility.SizeOf<T>() != slot.Size
                || UnsafeUtility.AlignOf<T>() != slot.Alignment)
                return BurstContextResult.TypeMismatch;
            if ((ulong)slot.Offset + slot.Size > (uint)instance.Semantic.TreeBlackboard.Length
                || access.ResolvedSlotIndex >= instance.TreeSlotVersions.Length || instance.TreeRevision.Length != 1)
                return BurstContextResult.InvalidHandle;

            var bytes = candidate.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
            if (!NativeBlackboardCanonicalV1.IsCanonical(program, slot, bytes.AsReadOnly()))
                return BurstContextResult.InvalidEncoding;
            if (NativeBlackboardCanonicalV1.EqualsCanonical(program, slot, instance.Semantic.TreeBlackboard, bytes.AsReadOnly()))
                return BurstContextResult.Success;
            if (instance.TreeSlotVersions[(int)access.ResolvedSlotIndex] == ulong.MaxValue || instance.TreeRevision[0] == ulong.MaxValue)
                return BurstContextResult.Overflow;

            NativeBlackboardCanonicalV1.CopyCanonical(program, slot, bytes.AsReadOnly(), instance.Semantic.TreeBlackboard);
            var versions = instance.TreeSlotVersions;
            versions[(int)access.ResolvedSlotIndex]++;
            var revision = instance.TreeRevision;
            revision[0]++;
            changed = true;
            return BurstContextResult.Success;
        }

        internal static BurstContextResult TryResolve(
            NativeProgramImageViewV2 program,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            BlackboardScope expectedScope,
            bool write,
            out NativeBlackboardAccessRecordV2 access,
            out NativeBlackboardSlotBindingV2 slot)
        {
            access = default; slot = default;
            if (nodeIndex >= program.NodeAccessRanges.Length) return BurstContextResult.InvalidHandle;
            var range = program.NodeAccessRanges[(int)nodeIndex];
            if (accessOrdinal >= range.Count || (ulong)range.Offset + accessOrdinal >= (uint)program.Accesses.Length)
                return BurstContextResult.InvalidHandle;
            access = program.Accesses[(int)(range.Offset + accessOrdinal)];
            if (access.NodeIndex != nodeIndex || access.AccessOrdinal != accessOrdinal) return BurstContextResult.InvalidHandle;
            if (access.Scope != expectedScope) return BurstContextResult.PhaseViolation;
            if (write ? access.Mode == NativeBlackboardAccessModeV2.Read : access.Mode == NativeBlackboardAccessModeV2.Write)
                return BurstContextResult.PhaseViolation;
            if (access.ResolvedSlotIndex >= program.Slots.Length) return BurstContextResult.InvalidHandle;
            slot = program.Slots[(int)access.ResolvedSlotIndex];
            if (slot.Scope != access.Scope || slot.ScopeSlotIndex != access.ScopeSlotIndex) return BurstContextResult.InvalidHandle;
            if (expectedType.TypeId != slot.TypeId || expectedType.Version != slot.TypeVersion
                || expectedType.Size != slot.Size || expectedType.Alignment != slot.Alignment
                || expectedType.EnumContractId != slot.EnumContractId
                || expectedType.RegisteredTypeIndex != slot.RegisteredTypeIndex)
                return BurstContextResult.TypeMismatch;
            if (expectedType.IsRegistered)
            {
                if (expectedType.RegisteredTypeIndex >= program.RegisteredTypes.Length) return BurstContextResult.TypeMismatch;
                var registered = program.RegisteredTypes[(int)expectedType.RegisteredTypeIndex];
                if (registered.Descriptor.TypeId != expectedType.TypeId || registered.Descriptor.Version != expectedType.Version
                    || registered.Descriptor.CanonicalSchemaId != expectedType.SchemaId
                    || registered.Descriptor.EqualityContractId != expectedType.EqualityContractId
                    || registered.SchemaHash != expectedType.SchemaHash)
                    return BurstContextResult.TypeMismatch;
            }
            return BurstContextResult.Success;
        }
    }

    internal static class NativeBlackboardCanonicalV1
    {
        internal static bool IsCanonical(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly bytes)
            => IsCanonical(program, slot, bytes, 0);

        internal static bool IsCanonical(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly bytes,
            uint sourceOffset)
        {
            if ((ulong)sourceOffset + slot.Size > (uint)bytes.Length) return false;
            if (slot.RegisteredTypeIndex != CompiledIndex.Invalid)
                return IsCanonicalRegistered(program, slot.RegisteredTypeIndex, bytes, sourceOffset, 0);
            return IsCanonicalBuiltIn(slot.TypeId, slot.EnumContractId, bytes, sourceOffset, slot.Size);
        }

        private static bool IsCanonicalRegistered(
            NativeProgramImageViewV2 program,
            uint registeredTypeIndex,
            NativeArray<byte>.ReadOnly bytes,
            uint sourceOffset,
            uint depth)
        {
            if (registeredTypeIndex >= program.RegisteredTypes.Length || depth >= program.RegisteredTypes.Length) return false;
            var type = program.RegisteredTypes[(int)registeredTypeIndex];
            var end = (ulong)type.FirstField + type.FieldCount;
            if (end > (uint)program.RegisteredFields.Length
                || (ulong)sourceOffset + (uint)type.Descriptor.Size > (uint)bytes.Length) return false;
            for (var fieldIndex = type.FirstField; fieldIndex < end; fieldIndex++)
            {
                var field = program.RegisteredFields[(int)fieldIndex];
                if ((ulong)field.Offset + field.Size > (uint)type.Descriptor.Size
                    || field.Offset % field.Alignment != 0
                    || !IsCanonicalField(program, field, bytes, sourceOffset, depth + 1)) return false;
                for (var otherIndex = fieldIndex + 1; otherIndex < end; otherIndex++)
                {
                    var other = program.RegisteredFields[(int)otherIndex];
                    if ((ulong)field.Offset < (ulong)other.Offset + other.Size
                        && (ulong)other.Offset < (ulong)field.Offset + field.Size) return false;
                }
            }
            for (var byteIndex = 0; byteIndex < type.Descriptor.Size; byteIndex++)
            {
                var covered = false;
                for (var fieldIndex = type.FirstField; fieldIndex < end; fieldIndex++)
                {
                    var field = program.RegisteredFields[(int)fieldIndex];
                    if ((uint)byteIndex >= field.Offset && (uint)byteIndex < field.Offset + field.Size) { covered = true; break; }
                }
                if (!covered && bytes[(int)sourceOffset + byteIndex] != 0) return false;
            }
            return true;
        }

        internal static bool EqualsCanonical(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte> current,
            NativeArray<byte>.ReadOnly candidate)
        {
            for (var index = 0; index < slot.Size; index++)
                if (current[(int)slot.Offset + index] != CanonicalByte(program, slot, candidate, (uint)index)) return false;
            return true;
        }

        internal static void CopyCanonical(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly source,
            NativeArray<byte> destination)
        {
            for (var index = 0; index < slot.Size; index++)
                destination[(int)slot.Offset + index] = CanonicalByte(program, slot, source, (uint)index);
        }

        private static byte CanonicalByte(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly source,
            uint index)
        {
            if (IsNegativeZeroComponent(program, slot, source, index)) return 0;
            return source[(int)index];
        }

        private static bool IsNegativeZeroComponent(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly source,
            uint index)
        {
            if (slot.RegisteredTypeIndex == CompiledIndex.Invalid)
            {
                if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float64)
                    return index == 7 && ReadU64(source, 0) == 0x8000000000000000ul;
                if ((index & 3) != 3 || !IsFloatType(slot.TypeId, out var componentSize, out var components)
                    || componentSize != 4) return false;
                var component = index / 4;
                return component < components && ReadU32(source, (int)(component * 4)) == 0x80000000u;
            }
            return IsNegativeZeroRegistered(program, slot.RegisteredTypeIndex, source, 0, index, 0);
        }

        private static bool IsNegativeZeroRegistered(
            NativeProgramImageViewV2 program,
            uint registeredTypeIndex,
            NativeArray<byte>.ReadOnly source,
            uint sourceOffset,
            uint targetOffset,
            uint depth)
        {
            if (registeredTypeIndex >= program.RegisteredTypes.Length || depth >= program.RegisteredTypes.Length)
                return false;
            var type = program.RegisteredTypes[(int)registeredTypeIndex];
            for (var fieldIndex = type.FirstField; fieldIndex < type.FirstField + type.FieldCount; fieldIndex++)
            {
                var field = program.RegisteredFields[(int)fieldIndex];
                var absolute = sourceOffset + field.Offset;
                if (targetOffset < absolute || targetOffset >= absolute + field.Size) continue;
                if (field.Encoding == NativeBlackboardFieldEncodingV2.Float32BitsLE
                    && targetOffset == absolute + 3 && ReadU32(source, (int)absolute) == 0x80000000u) return true;
                if (field.Encoding == NativeBlackboardFieldEncodingV2.Float64BitsLE
                    && targetOffset == absolute + 7 && ReadU64(source, (int)absolute) == 0x8000000000000000ul) return true;
                if (field.Encoding != NativeBlackboardFieldEncodingV2.Registered) return false;
                for (var nestedIndex = 0; nestedIndex < program.RegisteredTypes.Length; nestedIndex++)
                {
                    var nested = program.RegisteredTypes[nestedIndex];
                    if (nested.Descriptor.TypeId == field.ValueTypeId
                        && nested.Descriptor.Version == field.ValueTypeVersion
                        && nested.Descriptor.CanonicalSchemaId == field.RegisteredSchemaId
                        && nested.SchemaHash == field.RegisteredSchemaHash
                        && nested.Descriptor.EqualityContractId == field.EqualityContractId
                        && nested.Descriptor.Size == field.Size && nested.Descriptor.Alignment == field.Alignment)
                        return IsNegativeZeroRegistered(
                            program, (uint)nestedIndex, source, absolute, targetOffset, depth + 1);
                }
                return false;
            }
            return false;
        }

        private static bool IsCanonicalField(
            NativeProgramImageViewV2 program,
            NativeRegisteredBlackboardFieldRecordV2 field,
            NativeArray<byte>.ReadOnly bytes,
            uint sourceOffset,
            uint depth)
        {
            var offset = (int)(sourceOffset + field.Offset);
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Bool8) return bytes[offset] <= 1;
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Float32BitsLE) return Finite32(ReadU32(bytes, offset));
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Float64BitsLE) return Finite64(ReadU64(bytes, offset));
            if (field.Encoding == NativeBlackboardFieldEncodingV2.Registered)
            {
                for (var index = 0; index < program.RegisteredTypes.Length; index++)
                {
                    var nested = program.RegisteredTypes[index];
                    if (nested.Descriptor.TypeId == field.ValueTypeId
                        && nested.Descriptor.Version == field.ValueTypeVersion
                        && nested.Descriptor.CanonicalSchemaId == field.RegisteredSchemaId
                        && nested.SchemaHash == field.RegisteredSchemaHash
                        && nested.Descriptor.EqualityContractId == field.EqualityContractId
                        && nested.Descriptor.Size == field.Size && nested.Descriptor.Alignment == field.Alignment)
                        return IsCanonicalRegistered(program, (uint)index, bytes, (uint)offset, depth);
                }
                return false;
            }
            if (field.Encoding == NativeBlackboardFieldEncodingV2.FixedBytes)
                return IsCanonicalBuiltIn(field.ValueTypeId, 0, bytes, (uint)offset, field.Size);
            return true;
        }

        private static bool IsCanonicalBuiltIn(ulong typeId, ulong enumContractId, NativeArray<byte>.ReadOnly bytes, uint offset, uint size)
        {
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Bool) return bytes[(int)offset] <= 1;
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float32) return Finite32(ReadU32(bytes, (int)offset));
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float64) return Finite64(ReadU64(bytes, (int)offset));
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float2) return Finite32(ReadU32(bytes, (int)offset)) && Finite32(ReadU32(bytes, (int)offset + 4));
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float3) return Finite32(ReadU32(bytes, (int)offset)) && Finite32(ReadU32(bytes, (int)offset + 4)) && Finite32(ReadU32(bytes, (int)offset + 8));
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Quaternion) return Finite32(ReadU32(bytes, (int)offset)) && Finite32(ReadU32(bytes, (int)offset + 4)) && Finite32(ReadU32(bytes, (int)offset + 8)) && Finite32(ReadU32(bytes, (int)offset + 12));
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Enum32) return enumContractId != 0 && ReadU64(bytes, (int)offset) == enumContractId && Zero(bytes, (int)offset + 12, 4);
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.AgentId || typeId == NativeBuiltInBlackboardTypeIdsV1.EntityId) return ReadU64(bytes, (int)offset) != 0;
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.OperationId) return ReadU64(bytes, (int)offset) != 0 && ReadU32(bytes, (int)offset + 8) != uint.MaxValue;
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.AssetId)
            {
                var hasLocal = bytes[(int)offset + 24];
                return hasLocal <= 1 && (hasLocal != 0 || ReadU64(bytes, (int)offset + 16) == 0)
                    && Zero(bytes, (int)offset + 25, 7)
                    && (ReadU64(bytes, (int)offset) != 0 || ReadU64(bytes, (int)offset + 8) != 0);
            }
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.FixedString32 || typeId == NativeBuiltInBlackboardTypeIdsV1.FixedString64
                || typeId == NativeBuiltInBlackboardTypeIdsV1.FixedString128 || typeId == NativeBuiltInBlackboardTypeIdsV1.FixedString512)
            {
                var length = bytes[(int)offset] | bytes[(int)offset + 1] << 8;
                return length <= size - 2 && Zero(bytes, (int)offset + 2 + length, (int)size - 2 - length)
                    && ValidUtf8(bytes, (int)offset + 2, length);
            }
            return true;
        }

        private static bool IsFloatType(ulong typeId, out uint componentSize, out uint components)
        {
            componentSize = 4;
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float32) { components = 1; return true; }
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float2) { components = 2; return true; }
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Float3) { components = 3; return true; }
            if (typeId == NativeBuiltInBlackboardTypeIdsV1.Quaternion) { components = 4; return true; }
            components = 0; return false;
        }

        private static bool Finite32(uint bits) => (bits & 0x7f800000u) != 0x7f800000u;
        private static bool Finite64(ulong bits) => (bits & 0x7ff0000000000000ul) != 0x7ff0000000000000ul;
        private static uint ReadU32(NativeArray<byte>.ReadOnly bytes, int offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        private static ulong ReadU64(NativeArray<byte>.ReadOnly bytes, int offset)
            => ReadU32(bytes, offset) | (ulong)ReadU32(bytes, offset + 4) << 32;
        private static bool Zero(NativeArray<byte>.ReadOnly bytes, int offset, int count)
        { for (var index = 0; index < count; index++) if (bytes[offset + index] != 0) return false; return true; }

        private static bool ValidUtf8(NativeArray<byte>.ReadOnly bytes, int offset, int count)
        {
            var end = offset + count;
            for (var index = offset; index < end; index++)
            {
                var value = bytes[index];
                if (value < 0x80) continue;
                int extra; uint code;
                if (value >= 0xc2 && value <= 0xdf) { extra = 1; code = (uint)(value & 0x1f); }
                else if (value >= 0xe0 && value <= 0xef) { extra = 2; code = (uint)(value & 0x0f); }
                else if (value >= 0xf0 && value <= 0xf4) { extra = 3; code = (uint)(value & 0x07); }
                else return false;
                if (index + extra >= end) return false;
                for (var item = 0; item < extra; item++)
                {
                    var continuation = bytes[++index];
                    if ((continuation & 0xc0) != 0x80) return false;
                    code = (code << 6) | (uint)(continuation & 0x3f);
                }
                if (code > 0x10ffff || code >= 0xd800 && code <= 0xdfff
                    || extra == 2 && code < 0x800 || extra == 3 && code < 0x10000) return false;
            }
            return true;
        }
    }
}
