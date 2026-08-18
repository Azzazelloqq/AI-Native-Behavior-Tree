using AIBT.Burst;
using Unity.Collections;

namespace AIBT
{
    public static class NativeSharedReductionV1
    {
        public static BurstContextResult TryReduce(NativeSharedReductionViewV1 view)
        {
            if (!view.IsCreated || view.StreamCount > view.Streams.Length
                || view.Revision.Length != 1 || view.Versions.Length < view.Program.Slots.Length)
                return Fail(view, BurstContextResult.InvalidHandle,
                    NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                    NativeResourceKindV1.SharedContributionStreams);

            uint totalRecords = 0;
            uint reservedRecords = 0;
            uint reservedPayload = 0;
            for (var streamIndex = 0; streamIndex < view.StreamCount; streamIndex++)
            {
                var stream = view.Streams[(int)streamIndex];
                if (stream.UpdateId != view.UpdateId || stream.OwnerTreeInstanceId == 0
                    || stream.RecordCapacity == 0 || stream.PayloadCapacity == 0
                    || stream.FirstRecord != reservedRecords || stream.PayloadOffset != reservedPayload
                    || (ulong)stream.FirstRecord + stream.RecordCapacity > (uint)view.Records.Length
                    || (ulong)stream.PayloadOffset + stream.PayloadCapacity > (uint)view.Payload.Length
                    || stream.RecordCount > stream.RecordCapacity || stream.PayloadCount > stream.PayloadCapacity
                    || stream.NextSequence != stream.RecordCount
                    || stream.State != NativeSharedContributionStreamStateV1.Sealed
                        && stream.State != NativeSharedContributionStreamStateV1.Canceled
                    || stream.State == NativeSharedContributionStreamStateV1.Canceled
                        && (stream.RecordCount != 0 || stream.PayloadCount != 0)
                    || stream.Valid == 0)
                {
                    var code = stream.Valid == 0 && stream.FailureResource != NativeResourceKindV1.None
                        ? NativeRuntimeDiagnosticCodeV1.NativeOutputCapacityExceeded
                        : NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch;
                    var resource = stream.FailureResource == NativeResourceKindV1.None
                        ? NativeResourceKindV1.SharedContributionStreams : stream.FailureResource;
                    return Fail(view, stream.Valid == 0 ? BurstContextResult.CapacityExceeded : BurstContextResult.InvalidHandle,
                        code, resource);
                }
                reservedRecords += stream.RecordCapacity;
                reservedPayload += stream.PayloadCapacity;
                uint payloadCursor = stream.PayloadOffset;
                for (var recordIndex = 0u; recordIndex < stream.RecordCount; recordIndex++)
                {
                    if (totalRecords >= view.SortEntries.Length)
                        return Fail(view, BurstContextResult.CapacityExceeded,
                            NativeRuntimeDiagnosticCodeV1.NativeOutputCapacityExceeded,
                            NativeResourceKindV1.SharedReductionScratch);
                    var absolute = stream.FirstRecord + recordIndex;
                    var record = view.Records[(int)absolute];
                    var slot = default(NativeBlackboardSlotBindingV2);
                    if (record.TreeInstanceIdValue != stream.OwnerTreeInstanceId
                        || record.Sequence != (ulong)recordIndex
                        || record.RecordCapacity != stream.RecordCapacity
                        || record.PayloadCapacity != stream.PayloadCapacity
                        || record.PayloadOffset != payloadCursor
                        || record.PayloadLength == 0
                        || (ulong)record.PayloadOffset + record.PayloadLength
                            > (ulong)stream.PayloadOffset + stream.PayloadCapacity
                        || !TryFindSharedSlot(view.Program, record.ScopeSlotIndex, out _, out slot)
                        || record.TypeId != slot.TypeId || record.TypeVersion != slot.TypeVersion
                        || record.EnumContractId != slot.EnumContractId
                        || record.RegisteredTypeIndex != slot.RegisteredTypeIndex
                        || record.PayloadLength != slot.Size
                        || !ReducerAccepts(slot.Reduction, slot.TypeId)
                        || !NativeBlackboardCanonicalV1.IsCanonical(
                            view.Program, slot, view.Payload.AsReadOnly(), record.PayloadOffset))
                        return Fail(view, BurstContextResult.InvalidEncoding,
                            NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue,
                            NativeResourceKindV1.SharedContributionRecords);
                    payloadCursor += record.PayloadLength;
                    view.SortEntries[(int)totalRecords++] = absolute;
                }
                if (payloadCursor != stream.PayloadOffset + stream.PayloadCount)
                    return Fail(view, BurstContextResult.InvalidHandle,
                        NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                        NativeResourceKindV1.SharedContributionPayload);
            }

            SortBySemanticKey(view, totalRecords);
            for (var index = 1u; index < totalRecords; index++)
            {
                var left = view.Records[(int)view.SortEntries[(int)index - 1]];
                var right = view.Records[(int)view.SortEntries[(int)index]];
                if (left.TreeInstanceIdValue == right.TreeInstanceIdValue && left.Sequence == right.Sequence)
                    return Fail(view, BurstContextResult.InvalidHandle,
                        NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                        NativeResourceKindV1.SharedContributionRecords);
            }
            SortBySlotAndSemanticKey(view, totalRecords);

            for (var index = 0; index < view.Values.Length; index++)
                view.StagedValues[index] = view.Values[index];
            uint changedCount = 0;
            for (uint scopeSlot = 0; TryFindSharedSlot(view.Program, scopeSlot, out var resolvedSlot, out var slot); scopeSlot++)
            {
                var first = totalRecords;
                var count = 0u;
                for (var index = 0u; index < totalRecords; index++)
                {
                    var record = view.Records[(int)view.SortEntries[(int)index]];
                    if (record.ScopeSlotIndex != scopeSlot) continue;
                    if (first == totalRecords) first = index;
                    count++;
                }
                if (count == 0) continue;
                var reduction = ReduceSlot(view, slot, first, count);
                if (reduction != BurstContextResult.Success)
                    return Fail(view, reduction,
                        NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue,
                        NativeResourceKindV1.SharedContributionPayload);
                if (BytesEqual(view.Values, view.StagedValues, slot.Offset, slot.Size)) continue;
                if (view.Versions[(int)resolvedSlot] == ulong.MaxValue || view.Revision[0] == ulong.MaxValue)
                    return Fail(view, BurstContextResult.Overflow,
                        NativeRuntimeDiagnosticCodeV1.BlackboardVersionOverflow,
                        view.Versions[(int)resolvedSlot] == ulong.MaxValue
                            ? NativeResourceKindV1.InstanceSharedSlotVersions
                            : NativeResourceKindV1.InstanceSharedRevision);
                if (changedCount >= view.ChangedSlots.Length)
                    return Fail(view, BurstContextResult.CapacityExceeded,
                        NativeRuntimeDiagnosticCodeV1.NativeOutputCapacityExceeded,
                        NativeResourceKindV1.SharedCommitReport);
                view.ChangedSlots[(int)changedCount++] = scopeSlot;
            }

            if (changedCount != 0)
            {
                for (var index = 0; index < view.Values.Length; index++) view.Values[index] = view.StagedValues[index];
                for (var index = 0; index < changedCount; index++)
                {
                    TryFindSharedSlot(view.Program, view.ChangedSlots[index], out var resolvedSlot, out _);
                    view.Versions[(int)resolvedSlot]++;
                }
                view.Revision[0]++;
            }
            return Complete(view, changedCount);
        }

        private static BurstContextResult ReduceSlot(
            NativeSharedReductionViewV1 view,
            NativeBlackboardSlotBindingV2 slot,
            uint first,
            uint count)
        {
            var selected = view.Records[(int)view.SortEntries[(int)first]];
            if (slot.Reduction == NativeBlackboardReductionKindV2.First)
            { Copy(view.Payload, selected.PayloadOffset, view.StagedValues, slot.Offset, slot.Size); return BurstContextResult.Success; }
            if (slot.Reduction == NativeBlackboardReductionKindV2.Last)
            {
                selected = view.Records[(int)view.SortEntries[(int)(first + count - 1)]];
                Copy(view.Payload, selected.PayloadOffset, view.StagedValues, slot.Offset, slot.Size);
                return BurstContextResult.Success;
            }
            if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Bool)
            {
                var value = slot.Reduction == NativeBlackboardReductionKindV2.All;
                for (var index = 0u; index < count; index++)
                {
                    var record = view.Records[(int)view.SortEntries[(int)(first + index)]];
                    if (slot.Reduction == NativeBlackboardReductionKindV2.Any) value |= view.Payload[(int)record.PayloadOffset] != 0;
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.All) value &= view.Payload[(int)record.PayloadOffset] != 0;
                    else return BurstContextResult.TypeMismatch;
                }
                view.StagedValues[(int)slot.Offset] = value ? (byte)1 : (byte)0;
                return BurstContextResult.Success;
            }
            if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Int32)
            {
                var value = ReadI32(view.Payload, selected.PayloadOffset);
                for (var index = 1u; index < count; index++)
                {
                    var next = view.Records[(int)view.SortEntries[(int)(first + index)]];
                    var item = ReadI32(view.Payload, next.PayloadOffset);
                    if (slot.Reduction == NativeBlackboardReductionKindV2.Min) { if (item < value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Max) { if (item > value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Sum)
                    {
                        var sum = (long)value + item;
                        if (sum < int.MinValue || sum > int.MaxValue) return BurstContextResult.Overflow;
                        value = (int)sum;
                    }
                    else return BurstContextResult.TypeMismatch;
                }
                WriteI32(view.StagedValues, slot.Offset, value);
                return BurstContextResult.Success;
            }
            if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Int64)
            {
                var value = ReadI64(view.Payload, selected.PayloadOffset);
                for (var index = 1u; index < count; index++)
                {
                    var next = view.Records[(int)view.SortEntries[(int)(first + index)]];
                    var item = ReadI64(view.Payload, next.PayloadOffset);
                    if (slot.Reduction == NativeBlackboardReductionKindV2.Min) { if (item < value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Max) { if (item > value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Sum)
                    {
                        if (item > 0 && value > long.MaxValue - item || item < 0 && value < long.MinValue - item)
                            return BurstContextResult.Overflow;
                        value += item;
                    }
                    else return BurstContextResult.TypeMismatch;
                }
                WriteI64(view.StagedValues, slot.Offset, value);
                return BurstContextResult.Success;
            }
            if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float32)
            {
                var value = ReadF32(view.Payload, selected.PayloadOffset);
                for (var index = 1u; index < count; index++)
                {
                    var next = view.Records[(int)view.SortEntries[(int)(first + index)]];
                    var item = ReadF32(view.Payload, next.PayloadOffset);
                    if (slot.Reduction == NativeBlackboardReductionKindV2.Min) { if (item < value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Max) { if (item > value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Sum)
                    { value += item; if (!Finite(value)) return BurstContextResult.Overflow; if (value == 0f) value = 0f; }
                    else return BurstContextResult.TypeMismatch;
                }
                WriteF32(view.StagedValues, slot.Offset, value == 0f ? 0f : value);
                return BurstContextResult.Success;
            }
            if (slot.TypeId == NativeBuiltInBlackboardTypeIdsV1.Float64)
            {
                var value = ReadF64(view.Payload, selected.PayloadOffset);
                for (var index = 1u; index < count; index++)
                {
                    var next = view.Records[(int)view.SortEntries[(int)(first + index)]];
                    var item = ReadF64(view.Payload, next.PayloadOffset);
                    if (slot.Reduction == NativeBlackboardReductionKindV2.Min) { if (item < value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Max) { if (item > value) value = item; }
                    else if (slot.Reduction == NativeBlackboardReductionKindV2.Sum)
                    { value += item; if (!Finite(value)) return BurstContextResult.Overflow; if (value == 0d) value = 0d; }
                    else return BurstContextResult.TypeMismatch;
                }
                WriteF64(view.StagedValues, slot.Offset, value == 0d ? 0d : value);
                return BurstContextResult.Success;
            }
            return BurstContextResult.TypeMismatch;
        }

        private static bool ReducerAccepts(NativeBlackboardReductionKindV2 reduction, ulong typeId)
        {
            if (reduction == NativeBlackboardReductionKindV2.First || reduction == NativeBlackboardReductionKindV2.Last)
                return true;
            if (reduction == NativeBlackboardReductionKindV2.Any || reduction == NativeBlackboardReductionKindV2.All)
                return typeId == NativeBuiltInBlackboardTypeIdsV1.Bool;
            if (reduction == NativeBlackboardReductionKindV2.Min
                || reduction == NativeBlackboardReductionKindV2.Max
                || reduction == NativeBlackboardReductionKindV2.Sum)
                return typeId == NativeBuiltInBlackboardTypeIdsV1.Int32
                    || typeId == NativeBuiltInBlackboardTypeIdsV1.Int64
                    || typeId == NativeBuiltInBlackboardTypeIdsV1.Float32
                    || typeId == NativeBuiltInBlackboardTypeIdsV1.Float64;
            return false;
        }

        private static bool TryFindSharedSlot(
            NativeProgramImageViewV2 program, uint scopeSlotIndex,
            out uint resolvedSlot, out NativeBlackboardSlotBindingV2 slot)
        {
            resolvedSlot = 0; slot = default;
            var found = false;
            for (var index = 0; index < program.Slots.Length; index++)
            {
                var candidate = program.Slots[index];
                if (candidate.Scope != BlackboardScope.Shared || candidate.ScopeSlotIndex != scopeSlotIndex) continue;
                if (found) return false;
                found = true; resolvedSlot = (uint)index; slot = candidate;
            }
            return found;
        }

        private static void SortBySemanticKey(NativeSharedReductionViewV1 view, uint count)
        {
            for (var index = 1u; index < count; index++)
            {
                var value = view.SortEntries[(int)index]; var cursor = index;
                while (cursor != 0 && CompareSemantic(view, value, view.SortEntries[(int)cursor - 1]) < 0)
                { view.SortEntries[(int)cursor] = view.SortEntries[(int)cursor - 1]; cursor--; }
                view.SortEntries[(int)cursor] = value;
            }
        }

        private static void SortBySlotAndSemanticKey(NativeSharedReductionViewV1 view, uint count)
        {
            for (var index = 1u; index < count; index++)
            {
                var value = view.SortEntries[(int)index]; var cursor = index;
                while (cursor != 0 && CompareSlotSemantic(view, value, view.SortEntries[(int)cursor - 1]) < 0)
                { view.SortEntries[(int)cursor] = view.SortEntries[(int)cursor - 1]; cursor--; }
                view.SortEntries[(int)cursor] = value;
            }
        }

        private static int CompareSemantic(NativeSharedReductionViewV1 view, uint leftIndex, uint rightIndex)
        {
            var left = view.Records[(int)leftIndex]; var right = view.Records[(int)rightIndex];
            if (left.TreeInstanceIdValue < right.TreeInstanceIdValue) return -1;
            if (left.TreeInstanceIdValue > right.TreeInstanceIdValue) return 1;
            return left.Sequence < right.Sequence ? -1 : left.Sequence > right.Sequence ? 1 : 0;
        }

        private static int CompareSlotSemantic(NativeSharedReductionViewV1 view, uint leftIndex, uint rightIndex)
        {
            var left = view.Records[(int)leftIndex]; var right = view.Records[(int)rightIndex];
            if (left.ScopeSlotIndex < right.ScopeSlotIndex) return -1;
            if (left.ScopeSlotIndex > right.ScopeSlotIndex) return 1;
            return CompareSemantic(view, leftIndex, rightIndex);
        }

        private static BurstContextResult Complete(NativeSharedReductionViewV1 view, uint changedCount)
        {
            var control = view.Streams[0];
            control.ReductionResult = BurstContextResult.Success;
            control.ReductionFailureCode = NativeRuntimeDiagnosticCodeV1.None;
            control.ReductionFailureResource = NativeResourceKindV1.None;
            control.ChangedSlotCount = changedCount;
            view.Streams[0] = control;
            return BurstContextResult.Success;
        }

        private static BurstContextResult Fail(
            NativeSharedReductionViewV1 view, BurstContextResult result,
            NativeRuntimeDiagnosticCodeV1 code, NativeResourceKindV1 resource)
        {
            if (view.Streams.IsCreated && view.Streams.Length != 0)
            {
                var control = view.Streams[0];
                control.ReductionResult = result;
                control.ReductionFailureCode = code;
                control.ReductionFailureResource = resource;
                control.ChangedSlotCount = 0;
                view.Streams[0] = control;
            }
            return result;
        }

        private static bool BytesEqual(NativeArray<byte> left, NativeArray<byte> right, uint offset, uint count)
        { for (var index = 0u; index < count; index++) if (left[(int)(offset + index)] != right[(int)(offset + index)]) return false; return true; }
        private static void Copy(NativeArray<byte> source, uint sourceOffset, NativeArray<byte> target, uint targetOffset, uint count)
        { for (var index = 0u; index < count; index++) target[(int)(targetOffset + index)] = source[(int)(sourceOffset + index)]; }
        private static int ReadI32(NativeArray<byte> bytes, uint offset) => (int)ReadU32(bytes, offset);
        private static long ReadI64(NativeArray<byte> bytes, uint offset) => (long)ReadU64(bytes, offset);
        private static uint ReadU32(NativeArray<byte> bytes, uint offset)
            => (uint)(bytes[(int)offset] | bytes[(int)offset + 1] << 8 | bytes[(int)offset + 2] << 16 | bytes[(int)offset + 3] << 24);
        private static ulong ReadU64(NativeArray<byte> bytes, uint offset) => ReadU32(bytes, offset) | (ulong)ReadU32(bytes, offset + 4) << 32;
        private static float ReadF32(NativeArray<byte> bytes, uint offset) => System.BitConverter.Int32BitsToSingle(ReadI32(bytes, offset));
        private static double ReadF64(NativeArray<byte> bytes, uint offset) => System.BitConverter.Int64BitsToDouble(ReadI64(bytes, offset));
        private static void WriteI32(NativeArray<byte> bytes, uint offset, int value) => WriteU32(bytes, offset, (uint)value);
        private static void WriteI64(NativeArray<byte> bytes, uint offset, long value) => WriteU64(bytes, offset, (ulong)value);
        private static void WriteF32(NativeArray<byte> bytes, uint offset, float value) => WriteU32(bytes, offset, (uint)System.BitConverter.SingleToInt32Bits(value));
        private static void WriteF64(NativeArray<byte> bytes, uint offset, double value) => WriteU64(bytes, offset, (ulong)System.BitConverter.DoubleToInt64Bits(value));
        private static void WriteU32(NativeArray<byte> bytes, uint offset, uint value)
        { bytes[(int)offset] = (byte)value; bytes[(int)offset + 1] = (byte)(value >> 8); bytes[(int)offset + 2] = (byte)(value >> 16); bytes[(int)offset + 3] = (byte)(value >> 24); }
        private static void WriteU64(NativeArray<byte> bytes, uint offset, ulong value)
        { WriteU32(bytes, offset, (uint)value); WriteU32(bytes, offset + 4, (uint)(value >> 32)); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
