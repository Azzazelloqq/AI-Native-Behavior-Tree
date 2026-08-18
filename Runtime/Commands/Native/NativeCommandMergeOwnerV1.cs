using System;
using AIBT.Burst;
using Unity.Collections;

namespace AIBT
{
    public readonly struct NativeCommandMergeCapacityV1
    {
        public NativeCommandMergeCapacityV1(uint records, uint payloadBytes)
        {
            Records = records;
            PayloadBytes = payloadBytes;
        }

        public uint Records { get; }
        public uint PayloadBytes { get; }
        internal bool IsValid => Records != 0 && Records <= int.MaxValue && PayloadBytes <= int.MaxValue;
    }

    public sealed class NativeCommandMergeOwnerV1
    {
        private NativeArray<NativeCommandMergeControlV1> _control;
        private NativeArray<NativeCommandRecordV1> _stagingRecords;
        private NativeArray<byte> _stagingPayload;
        private NativeArray<NativeCommandRecordV1> _outputRecords;
        private NativeArray<byte> _outputPayload;
        private uint _stagingCount;
        private uint _stagingPayloadCount;
        private uint _outputCount;
        private uint _outputPayloadCount;
        private byte _state;

        private NativeCommandMergeOwnerV1()
        {
        }

        public static BurstContextResult TryCreate(
            NativeCommandMergeCapacityV1 capacity,
            Allocator allocator,
            out NativeCommandMergeOwnerV1 owner)
        {
            owner = null;
            if (!capacity.IsValid || allocator != Allocator.Persistent)
            {
                return BurstContextResult.InvalidEncoding;
            }

            var created = new NativeCommandMergeOwnerV1();
            try
            {
                created._control = Allocate<NativeCommandMergeControlV1>(1, allocator);
                created._stagingRecords = Allocate<NativeCommandRecordV1>(capacity.Records, allocator);
                created._stagingPayload = Allocate<byte>(capacity.PayloadBytes, allocator);
                created._outputRecords = Allocate<NativeCommandRecordV1>(capacity.Records, allocator);
                created._outputPayload = Allocate<byte>(capacity.PayloadBytes, allocator);
                created._state = 1;
                created._control[0] = new NativeCommandMergeControlV1 { State = 1, Epoch = 1 };
                created.WarmBorrowedViewSurfaces();
                owner = created;
                return BurstContextResult.Success;
            }
            catch (Exception)
            {
                created.DisposeArrays();
                return BurstContextResult.CapacityExceeded;
            }
        }

        public BurstContextResult TryAddStream(in NativeCommandStreamViewV1 stream)
        {
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            if (_state == 2) return BurstContextResult.AlreadyCommitted;
            if (!stream.IsLiveForMerge) return BurstContextResult.InvalidHandle;
            if ((ulong)_stagingCount + stream.Count > (ulong)_stagingRecords.Length)
                return BurstContextResult.CapacityExceeded;

            var requiredPayload = 0ul;
            for (var index = 0u; index < stream.Count; index++)
            {
                if (stream.TryGetRecord(index, out var record) != BurstContextResult.Success)
                    return BurstContextResult.InvalidHandle;
                if (!IsRecordValid(record, stream.PayloadCount)) return BurstContextResult.InvalidEncoding;
                requiredPayload += record.PayloadSize;
                if (requiredPayload > uint.MaxValue) return BurstContextResult.Overflow;
            }

            if ((ulong)_stagingPayloadCount + requiredPayload > (ulong)_stagingPayload.Length)
                return BurstContextResult.CapacityExceeded;

            var recordCursor = _stagingCount;
            var payloadCursor = _stagingPayloadCount;
            for (var index = 0u; index < stream.Count; index++)
            {
                stream.TryGetRecord(index, out var source);
                var destinationOffset = source.PayloadSize == 0 ? 0u : payloadCursor;
                for (var payloadIndex = 0u; payloadIndex < source.PayloadSize; payloadIndex++)
                {
                    stream.TryGetPayloadByte(source.PayloadOffset + payloadIndex, out var value);
                    _stagingPayload[(int)(payloadCursor + payloadIndex)] = value;
                }

                _stagingRecords[(int)recordCursor++] = new NativeCommandRecordV1(
                    source.CommandType,
                    source.OperationId,
                    destinationOffset,
                    source.PayloadSize,
                    source.Phase,
                    source.TreeInstanceId,
                    source.Sequence);
                payloadCursor += source.PayloadSize;
            }

            _stagingCount = recordCursor;
            _stagingPayloadCount = payloadCursor;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryFinalize(out NativeMergedCommandViewV1 output)
        {
            output = default;
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            if (_state == 2) return BurstContextResult.AlreadyCommitted;
            if (_stagingCount > (uint)_outputRecords.Length || _stagingPayloadCount > (uint)_outputPayload.Length)
                return BurstContextResult.CapacityExceeded;

            for (var index = 0u; index < _stagingCount; index++)
            {
                var record = _stagingRecords[(int)index];
                if (!IsRecordValid(record, _stagingPayloadCount)) return BurstContextResult.InvalidEncoding;
                for (var other = index + 1; other < _stagingCount; other++)
                {
                    var candidate = _stagingRecords[(int)other];
                    if (record.TreeInstanceId == candidate.TreeInstanceId && record.Sequence == candidate.Sequence)
                    {
                        return BurstContextResult.InvalidStatus;
                    }
                }
            }

            Sort(_stagingRecords, _stagingCount);
            var outputPayloadCursor = 0u;
            for (var index = 0u; index < _stagingCount; index++)
            {
                var source = _stagingRecords[(int)index];
                var destinationOffset = source.PayloadSize == 0 ? 0u : outputPayloadCursor;
                for (var payloadIndex = 0u; payloadIndex < source.PayloadSize; payloadIndex++)
                {
                    _outputPayload[(int)(outputPayloadCursor + payloadIndex)] =
                        _stagingPayload[(int)(source.PayloadOffset + payloadIndex)];
                }

                _outputRecords[(int)index] = new NativeCommandRecordV1(
                    source.CommandType,
                    source.OperationId,
                    destinationOffset,
                    source.PayloadSize,
                    source.Phase,
                    source.TreeInstanceId,
                    source.Sequence);
                outputPayloadCursor += source.PayloadSize;
            }

            _outputCount = _stagingCount;
            _outputPayloadCount = outputPayloadCursor;
            _state = 2;
            var control = _control[0];
            control.State = 2;
            _control[0] = control;
            output = new NativeMergedCommandViewV1(
                _control.AsReadOnly(),
                control.Epoch,
                _outputRecords.AsReadOnly(),
                _outputCount,
                _outputPayload.AsReadOnly(),
                _outputPayloadCount);
            return BurstContextResult.Success;
        }

        public BurstContextResult TryGetOutput(out NativeMergedCommandViewV1 output)
        {
            output = default;
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            if (_state != 2) return BurstContextResult.PhaseViolation;
            var control = _control[0];
            output = new NativeMergedCommandViewV1(
                _control.AsReadOnly(),
                control.Epoch,
                _outputRecords.AsReadOnly(),
                _outputCount,
                _outputPayload.AsReadOnly(),
                _outputPayloadCount);
            return BurstContextResult.Success;
        }

        public BurstContextResult TryReset()
        {
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            var control = _control[0];
            if (control.Epoch == ulong.MaxValue) return BurstContextResult.Overflow;
            _stagingCount = 0;
            _stagingPayloadCount = 0;
            _outputCount = 0;
            _outputPayloadCount = 0;
            _state = 1;
            control.State = 1;
            control.Epoch++;
            _control[0] = control;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryDispose()
        {
            if (_state == 0 || _state == 3) return BurstContextResult.InvalidHandle;
            _state = 3;
            var control = _control[0];
            control.State = 3;
            _control[0] = control;
            DisposeArrays();
            return BurstContextResult.Success;
        }

        private static bool IsRecordValid(in NativeCommandRecordV1 record, uint payloadCount)
        {
            if (!record.CommandType.IsValid
                || !record.TreeInstanceId.IsValid
                || record.Sequence == 0
                || (record.Phase != CommandPhase.Execute && record.Phase != CommandPhase.Cancel))
            {
                return false;
            }

            if (record.OperationId.IsValid && record.OperationId.TreeInstanceId != record.TreeInstanceId)
                return false;
            if (record.PayloadSize == 0) return record.PayloadOffset == 0;
            return (ulong)record.PayloadOffset + record.PayloadSize <= payloadCount;
        }

        private static void Sort(NativeArray<NativeCommandRecordV1> values, uint count)
        {
            for (var index = 1u; index < count; index++)
            {
                var value = values[(int)index];
                var cursor = (int)index - 1;
                while (cursor >= 0 && Compare(values[cursor], value) > 0)
                {
                    values[cursor + 1] = values[cursor];
                    cursor--;
                }

                values[cursor + 1] = value;
            }
        }

        private static int Compare(in NativeCommandRecordV1 left, in NativeCommandRecordV1 right)
        {
            var result = left.Phase.CompareTo(right.Phase);
            if (result != 0) return result;
            result = left.TreeInstanceId.CompareTo(right.TreeInstanceId);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }

        private void DisposeArrays()
        {
            Dispose(ref _outputPayload);
            Dispose(ref _outputRecords);
            Dispose(ref _stagingPayload);
            Dispose(ref _stagingRecords);
            Dispose(ref _control);
        }

        private void WarmBorrowedViewSurfaces()
        {
            _ = _control.AsReadOnly().Length;
            _ = _outputRecords.AsReadOnly().Length;
            _ = _outputPayload.AsReadOnly().Length;
        }

        private static NativeArray<T> Allocate<T>(uint capacity, Allocator allocator) where T : struct
        {
#if UNITY_INCLUDE_TESTS
            NativeCommandAsyncTestHooksV1.BeforeAllocation();
#endif
            return new NativeArray<T>((int)capacity, allocator, NativeArrayOptions.ClearMemory);
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        {
            if (!value.IsCreated) return;
            value.Dispose();
            value = default;
        }
    }
}
