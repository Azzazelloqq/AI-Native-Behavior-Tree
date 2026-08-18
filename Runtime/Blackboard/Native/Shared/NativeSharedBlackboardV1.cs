using AIBT.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT
{
    public static class NativeSharedBlackboardV1
    {
        public static BurstContextResult TryRead<T>(
            NativeSharedExecutionViewV1 view,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            out T value,
            out ulong version)
            where T : unmanaged
        {
            value = default;
            version = 0;
            if (!view.IsValid) return BurstContextResult.PhaseViolation;
            var validation = NativeTreeBlackboardV1.TryResolve(
                view.Program,
                nodeIndex,
                accessOrdinal,
                expectedType,
                BlackboardScope.Shared,
                false,
                out var access,
                out var slot);
            if (validation != BurstContextResult.Success) return validation;
            if (UnsafeUtility.SizeOf<T>() != slot.Size || UnsafeUtility.AlignOf<T>() != slot.Alignment)
                return BurstContextResult.TypeMismatch;
            if ((ulong)slot.Offset + slot.Size > (uint)view.Context.Values.Length
                || access.ResolvedSlotIndex >= view.Context.SlotVersions.Length)
                return BurstContextResult.InvalidHandle;
            value = new NativeSlice<byte>(
                view.Context.MutableValues,
                (int)slot.Offset,
                (int)slot.Size).SliceConvert<T>()[0];
            version = view.Context.SlotVersions[(int)access.ResolvedSlotIndex];
            return BurstContextResult.Success;
        }

        public static BurstContextResult TryWrite<T>(
            NativeSharedExecutionViewV1 view,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            NativeArray<T> candidate,
            out bool changed)
            where T : unmanaged
        {
            changed = false;
            if (!view.IsValid) return BurstContextResult.PhaseViolation;
            return BurstContextResult.PhaseViolation;
        }

        public static BurstContextResult TryContribute<T>(
            NativeSharedContributionWriterV1 writer,
            uint nodeIndex,
            uint accessOrdinal,
            NativeBlackboardTypeIdV2 expectedType,
            NativeArray<T> candidate)
            where T : unmanaged
        {
            if (!TryGetActiveStream(writer, out var stream)) return BurstContextResult.PhaseViolation;
            var validation = NativeTreeBlackboardV1.TryResolve(
                writer.Program, nodeIndex, accessOrdinal, expectedType, BlackboardScope.Shared, true,
                out _, out var slot);
            if (validation != BurstContextResult.Success)
            {
                Invalidate(writer, ref stream, NativeResourceKindV1.SharedContributionRecords);
                return validation;
            }
            if (!candidate.IsCreated || candidate.Length != 1
                || UnsafeUtility.SizeOf<T>() != slot.Size || UnsafeUtility.AlignOf<T>() != slot.Alignment)
            {
                Invalidate(writer, ref stream, NativeResourceKindV1.SharedContributionPayload);
                return BurstContextResult.TypeMismatch;
            }
            var source = candidate.Reinterpret<byte>(UnsafeUtility.SizeOf<T>()).AsReadOnly();
            if (!NativeBlackboardCanonicalV1.IsCanonical(writer.Program, slot, source))
            {
                Invalidate(writer, ref stream, NativeResourceKindV1.SharedContributionPayload);
                return BurstContextResult.InvalidEncoding;
            }
            if (stream.NextSequence == ulong.MaxValue)
            {
                Invalidate(writer, ref stream, NativeResourceKindV1.SharedContributionRecords);
                return BurstContextResult.Overflow;
            }
            if (stream.RecordCount >= stream.RecordCapacity
                || (ulong)stream.FirstRecord + stream.RecordCount >= (uint)writer.Records.Length)
            {
                Invalidate(writer, ref stream, NativeResourceKindV1.SharedContributionRecords);
                return BurstContextResult.CapacityExceeded;
            }
            if ((ulong)stream.PayloadCount + slot.Size > stream.PayloadCapacity
                || (ulong)stream.PayloadOffset + stream.PayloadCount + slot.Size > (uint)writer.Payload.Length)
            {
                Invalidate(writer, ref stream, NativeResourceKindV1.SharedContributionPayload);
                return BurstContextResult.CapacityExceeded;
            }

            var recordIndex = stream.FirstRecord + stream.RecordCount;
            var payloadOffset = stream.PayloadOffset + stream.PayloadCount;
            CopyCanonical(writer.Program, slot, source, writer.Payload, payloadOffset);
            var records = writer.Records;
            records[(int)recordIndex] = new NativeSharedContributionRecordV1(
                slot.ScopeSlotIndex,
                stream.OwnerTreeInstanceId,
                stream.NextSequence,
                slot.TypeId,
                slot.TypeVersion,
                slot.EnumContractId,
                slot.RegisteredTypeIndex,
                stream.RecordCapacity,
                stream.PayloadCapacity,
                payloadOffset,
                slot.Size);
            stream.RecordCount++;
            stream.PayloadCount += slot.Size;
            stream.NextSequence++;
            var streams = writer.Streams;
            streams[(int)writer.StreamIndex] = stream;
            return BurstContextResult.Success;
        }

        private static bool TryGetActiveStream(
            NativeSharedContributionWriterV1 writer,
            out NativeSharedContributionStreamV1 stream)
        {
            stream = default;
            if (!writer.IsCreated) return false;
            stream = writer.Streams[(int)writer.StreamIndex];
            return stream.UpdateId == writer.UpdateId && stream.ActiveLeaseId == writer.LeaseId
                && stream.State == NativeSharedContributionStreamStateV1.Active && stream.Valid != 0;
        }

        private static void Invalidate(
            NativeSharedContributionWriterV1 writer,
            ref NativeSharedContributionStreamV1 stream,
            NativeResourceKindV1 resource)
        {
            stream.Valid = 0;
            stream.FailureResource = resource;
            var streams = writer.Streams;
            streams[(int)writer.StreamIndex] = stream;
        }

        private static void CopyCanonical(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly source,
            NativeArray<byte> destination,
            uint destinationOffset)
        {
            for (uint index = 0; index < slot.Size; index++)
                destination[(int)(destinationOffset + index)] = IsNegativeZero(program, slot, source, index) ? (byte)0 : source[(int)index];
        }

        private static bool IsNegativeZero(
            NativeProgramImageViewV2 program,
            NativeBlackboardSlotBindingV2 slot,
            NativeArray<byte>.ReadOnly source,
            uint index)
        {
            if (slot.RegisteredTypeIndex == CompiledIndex.Invalid)
            {
                if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float64)
                    return index == 7 && ReadU64(source, 0) == 0x8000000000000000ul;
                uint components;
                if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float32) components = 1;
                else if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float2) components = 2;
                else if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float3) components = 3;
                else if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Quaternion) components = 4;
                else return false;
                return (index & 3) == 3 && index / 4 < components
                    && ReadU32(source, (int)(index / 4 * 4)) == 0x80000000u;
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
            if (registeredTypeIndex >= program.RegisteredTypes.Length || depth >= program.RegisteredTypes.Length) return false;
            var type = program.RegisteredTypes[(int)registeredTypeIndex];
            for (var fieldIndex = type.FirstField; fieldIndex < type.FirstField + type.FieldCount; fieldIndex++)
            {
                var field = program.RegisteredFields[(int)fieldIndex];
                var absolute = sourceOffset + field.Offset;
                if (targetOffset < absolute || targetOffset >= absolute + field.Size) continue;
                if (field.Encoding == NativeBlackboardFieldEncodingV2.Float32BitsLE)
                    return targetOffset == absolute + 3 && ReadU32(source, (int)absolute) == 0x80000000u;
                if (field.Encoding == NativeBlackboardFieldEncodingV2.Float64BitsLE)
                    return targetOffset == absolute + 7 && ReadU64(source, (int)absolute) == 0x8000000000000000ul;
                if (field.Encoding != NativeBlackboardFieldEncodingV2.Registered) return false;
                for (var nestedIndex = 0; nestedIndex < program.RegisteredTypes.Length; nestedIndex++)
                {
                    var nested = program.RegisteredTypes[nestedIndex];
                    if (nested.Descriptor.TypeId == field.ValueTypeId
                        && nested.Descriptor.Version == field.ValueTypeVersion
                        && nested.Descriptor.CanonicalSchemaId == field.RegisteredSchemaId
                        && nested.SchemaHash == field.RegisteredSchemaHash
                        && nested.Descriptor.EqualityContractId == field.EqualityContractId)
                        return IsNegativeZeroRegistered(
                            program, (uint)nestedIndex, source, absolute, targetOffset, depth + 1);
                }
                return false;
            }
            return false;
        }

        private static uint ReadU32(NativeArray<byte>.ReadOnly bytes, int offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);

        private static ulong ReadU64(NativeArray<byte>.ReadOnly bytes, int offset)
            => ReadU32(bytes, offset) | (ulong)ReadU32(bytes, offset + 4) << 32;
    }
}
