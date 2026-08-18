using System;
using AIBT.Burst;
using Unity.Collections;

namespace AIBT
{
    public enum NativeOperationStateV1 : byte
    {
        Empty = 0,
        Active = 1,
        Consumed = 2,
        Cancelled = 3,
    }

    public enum NativeCommandAsyncDiagnosticCodeV1 : ushort
    {
        None = 0,
        DuplicateCompletionOrderingKey = 4101,
        NonIncreasingSourceSequence = 4102,
        UnknownOperation = 4103,
        StaleOperationGeneration = 4104,
        CancelledOperation = 4105,
        AlreadyConsumedOperation = 4106,
        CompletionPayloadMismatch = 4110,
    }

    // AIBT4101-AIBT4106 and AIBT4110 are persisted normalization diagnostics. Managed P1
    // AIBT4107-AIBT4109 map in the native hot API to stable BurstContextResult values instead:
    // Overflow for operation/command sequence exhaustion and InvalidEncoding for invalid commands.

    public readonly struct NativeCommandAsyncCapacityV1
    {
        public NativeCommandAsyncCapacityV1(
            uint operationRecords,
            uint operationCancellationPayloadBytes,
            uint completionInputRecords,
            uint pendingCompletionRecords,
            uint completionPayloadBytes,
            uint completionSources,
            uint diagnosticRecords,
            uint executeCommandRecords,
            uint cancelCommandRecords,
            uint commandPayloadBytes)
        {
            OperationRecords = operationRecords;
            OperationCancellationPayloadBytes = operationCancellationPayloadBytes;
            CompletionInputRecords = completionInputRecords;
            PendingCompletionRecords = pendingCompletionRecords;
            CompletionPayloadBytes = completionPayloadBytes;
            CompletionSources = completionSources;
            DiagnosticRecords = diagnosticRecords;
            ExecuteCommandRecords = executeCommandRecords;
            CancelCommandRecords = cancelCommandRecords;
            CommandPayloadBytes = commandPayloadBytes;
        }

        public uint OperationRecords { get; }
        public uint OperationCancellationPayloadBytes { get; }
        public uint CompletionInputRecords { get; }
        public uint PendingCompletionRecords { get; }
        public uint CompletionPayloadBytes { get; }
        public uint CompletionSources { get; }
        public uint DiagnosticRecords { get; }
        public uint ExecuteCommandRecords { get; }
        public uint CancelCommandRecords { get; }
        public uint CommandPayloadBytes { get; }

        internal bool IsValid
            => OperationRecords != 0
                && CompletionInputRecords != 0
                && PendingCompletionRecords != 0
                && CompletionSources != 0
                && DiagnosticRecords != 0
                && ExecuteCommandRecords != 0
                && CancelCommandRecords != 0
                && OperationRecords <= int.MaxValue
                && OperationCancellationPayloadBytes <= int.MaxValue
                && CompletionInputRecords <= int.MaxValue
                && PendingCompletionRecords <= int.MaxValue
                && CompletionPayloadBytes <= int.MaxValue
                && CompletionSources <= int.MaxValue
                && DiagnosticRecords <= int.MaxValue
                && ExecuteCommandRecords <= int.MaxValue
                && CancelCommandRecords <= int.MaxValue
                && CommandPayloadBytes <= int.MaxValue;
    }

    public readonly struct NativePayloadSliceV1
    {
        public NativePayloadSliceV1(NativeArray<byte>.ReadOnly bytes, uint offset, uint size)
        {
            Bytes = bytes;
            Offset = offset;
            Size = size;
        }

        public NativeArray<byte>.ReadOnly Bytes { get; }
        public uint Offset { get; }
        public uint Size { get; }
        public static NativePayloadSliceV1 Empty => default;

        internal bool IsValid
        {
            get
            {
                if (Size == 0)
                {
                    return Offset == 0;
                }

                return Bytes.IsCreated && (ulong)Offset + Size <= (ulong)Bytes.Length;
            }
        }
    }

    public readonly struct NativeCompletionInputRecordV1
    {
        public NativeCompletionInputRecordV1(
            OperationId operationId,
            CompletionOutcome outcome,
            CompletionPayloadType payloadType,
            uint payloadOffset,
            uint payloadSize,
            ulong sourceId,
            ulong sourceSequence,
            Revision snapshotRevision)
        {
            OperationId = operationId;
            Outcome = outcome;
            PayloadType = payloadType;
            PayloadOffset = payloadOffset;
            PayloadSize = payloadSize;
            SourceId = sourceId;
            SourceSequence = sourceSequence;
            SnapshotRevision = snapshotRevision;
        }

        public OperationId OperationId { get; }
        public CompletionOutcome Outcome { get; }
        public CompletionPayloadType PayloadType { get; }
        public uint PayloadOffset { get; }
        public uint PayloadSize { get; }
        public ulong SourceId { get; }
        public ulong SourceSequence { get; }
        public Revision SnapshotRevision { get; }
    }

    public readonly struct NativeCompletionExpectationV1
    {
        private NativeCompletionExpectationV1(byte mode, CompletionPayloadType payloadType, uint payloadSize)
        {
            Mode = mode;
            PayloadType = payloadType;
            PayloadSize = payloadSize;
        }

        internal byte Mode { get; }
        internal CompletionPayloadType PayloadType { get; }
        internal uint PayloadSize { get; }
        public static NativeCompletionExpectationV1 Any => default;
        public static NativeCompletionExpectationV1 NoPayload => new NativeCompletionExpectationV1(1, default, 0);

        public static NativeCompletionExpectationV1 Typed(CompletionPayloadType payloadType, uint payloadSize)
        {
            return payloadType.IsValid && payloadSize != 0
                ? new NativeCompletionExpectationV1(2, payloadType, payloadSize)
                : new NativeCompletionExpectationV1(byte.MaxValue, default, 0);
        }

        internal bool IsValid => Mode <= 2 && (Mode != 2 || (PayloadType.IsValid && PayloadSize != 0));

        internal bool Matches(in NativePendingCompletionRecordV1 record)
        {
            if (Mode == 0) return true;
            if (Mode == 1) return !record.PayloadType.IsValid && record.PayloadSize == 0;
            return record.PayloadType == PayloadType && record.PayloadSize == PayloadSize;
        }
    }

    public readonly struct NativeCompletionDiagnosticV1
    {
        internal NativeCompletionDiagnosticV1(
            NativeCommandAsyncDiagnosticCodeV1 code,
            OperationId operationId,
            ulong sourceId,
            ulong sourceSequence)
        {
            Code = code;
            OperationId = operationId;
            SourceId = sourceId;
            SourceSequence = sourceSequence;
        }

        public NativeCommandAsyncDiagnosticCodeV1 Code { get; }
        public OperationId OperationId { get; }
        public ulong SourceId { get; }
        public ulong SourceSequence { get; }
    }

    /// <summary>
    /// Borrowed completion payload valid only for the captured lease and completion epoch while
    /// owner storage is alive. A later normalization or lease release invalidates every copy.
    /// Calling it after successful owner disposal is outside the ABI lifetime contract.
    /// </summary>
    public readonly struct NativeConsumedCompletionV1
    {
        private readonly NativeArray<byte>.ReadOnly _payload;
        private readonly NativeArray<NativeCommandAsyncControlV1>.ReadOnly _control;
        private readonly ulong _ownerId;
        private readonly uint _generation;
        private readonly ulong _leaseId;
        private readonly ulong _completionEpoch;

        internal NativeConsumedCompletionV1(
            in NativePendingCompletionRecordV1 source,
            NativeArray<byte>.ReadOnly payload,
            NativeArray<NativeCommandAsyncControlV1>.ReadOnly control,
            ulong ownerId,
            uint generation,
            ulong leaseId,
            ulong completionEpoch)
        {
            OperationId = source.OperationId;
            Outcome = source.Outcome;
            PayloadType = source.PayloadType;
            PayloadOffset = source.PayloadOffset;
            PayloadSize = source.PayloadSize;
            SourceId = source.SourceId;
            SourceSequence = source.SourceSequence;
            SnapshotRevision = source.SnapshotRevision;
            _payload = payload;
            _control = control;
            _ownerId = ownerId;
            _generation = generation;
            _leaseId = leaseId;
            _completionEpoch = completionEpoch;
        }

        public OperationId OperationId { get; }
        public CompletionOutcome Outcome { get; }
        public CompletionPayloadType PayloadType { get; }
        public uint PayloadOffset { get; }
        public uint PayloadSize { get; }
        public ulong SourceId { get; }
        public ulong SourceSequence { get; }
        public Revision SnapshotRevision { get; }

        public BurstContextResult TryGetPayloadByte(uint index, out byte value)
        {
            value = 0;
            if (!_control.IsCreated)
            {
                return BurstContextResult.InvalidHandle;
            }

            var control = _control[0];
            if (control.State != 2
                || control.OwnerId != _ownerId
                || control.Generation != _generation
                || control.ActiveLeaseId != _leaseId
                || control.CompletionEpoch != _completionEpoch
                || index >= PayloadSize
                || !_payload.IsCreated
                || (ulong)PayloadOffset + PayloadSize > (ulong)_payload.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            value = _payload[(int)(PayloadOffset + index)];
            return BurstContextResult.Success;
        }
    }

    public readonly struct NativeCommandRecordV1
    {
        internal NativeCommandRecordV1(
            CommandType commandType,
            OperationId operationId,
            uint payloadOffset,
            uint payloadSize,
            CommandPhase phase,
            TreeInstanceId treeInstanceId,
            ulong sequence)
        {
            CommandType = commandType;
            OperationId = operationId;
            PayloadOffset = payloadOffset;
            PayloadSize = payloadSize;
            Phase = phase;
            TreeInstanceId = treeInstanceId;
            Sequence = sequence;
        }

        public CommandType CommandType { get; }
        public OperationId OperationId { get; }
        public uint PayloadOffset { get; }
        public uint PayloadSize { get; }
        public CommandPhase Phase { get; }
        public TreeInstanceId TreeInstanceId { get; }
        public ulong Sequence { get; }
    }

    /// <summary>
    /// Borrowed command stream valid only for the captured lease and command epoch while owner
    /// storage is alive. Command publication changes, lease release, or reset invalidate copies.
    /// Calling it after successful owner disposal is outside the ABI lifetime contract.
    /// </summary>
    public readonly struct NativeCommandStreamViewV1
    {
        private readonly NativeArray<NativeCommandAsyncControlV1>.ReadOnly _control;
        private readonly ulong _ownerId;
        private readonly uint _generation;
        private readonly ulong _leaseId;
        private readonly ulong _nextCommandSequence;
        private readonly byte _commandSequenceExhausted;
        private readonly NativeArray<NativeCommandRecordV1>.ReadOnly _execute;
        private readonly NativeArray<NativeCommandRecordV1>.ReadOnly _cancel;
        private readonly NativeArray<byte>.ReadOnly _payload;

        internal NativeCommandStreamViewV1(
            NativeArray<NativeCommandAsyncControlV1>.ReadOnly control,
            ulong ownerId,
            uint generation,
            ulong leaseId,
            ulong nextCommandSequence,
            byte commandSequenceExhausted,
            NativeArray<NativeCommandRecordV1>.ReadOnly execute,
            uint executeCount,
            NativeArray<NativeCommandRecordV1>.ReadOnly cancel,
            uint cancelCount,
            NativeArray<byte>.ReadOnly payload,
            uint payloadCount)
        {
            _control = control;
            _ownerId = ownerId;
            _generation = generation;
            _leaseId = leaseId;
            _nextCommandSequence = nextCommandSequence;
            _commandSequenceExhausted = commandSequenceExhausted;
            _execute = execute;
            _cancel = cancel;
            _payload = payload;
            ExecuteCount = executeCount;
            CancelCount = cancelCount;
            PayloadCount = payloadCount;
        }

        public uint ExecuteCount { get; }
        public uint CancelCount { get; }
        public uint Count => ExecuteCount + CancelCount;
        public uint PayloadCount { get; }
        internal bool IsLiveForMerge => IsLive();

        public BurstContextResult TryGetRecord(uint index, out NativeCommandRecordV1 record)
        {
            record = default;
            if (!IsLive()) return BurstContextResult.InvalidHandle;
            if (index < ExecuteCount)
            {
                if (!_execute.IsCreated || index >= (uint)_execute.Length) return BurstContextResult.InvalidHandle;
                record = _execute[(int)index];
                return BurstContextResult.Success;
            }

            var cancelIndex = index - ExecuteCount;
            if (!_cancel.IsCreated || cancelIndex >= CancelCount || cancelIndex >= (uint)_cancel.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            record = _cancel[(int)cancelIndex];
            return BurstContextResult.Success;
        }

        public BurstContextResult TryGetPayloadByte(uint index, out byte value)
        {
            value = 0;
            if (!IsLive() || !_payload.IsCreated || index >= PayloadCount || index >= (uint)_payload.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            value = _payload[(int)index];
            return BurstContextResult.Success;
        }

        private bool IsLive()
        {
            if (!_control.IsCreated) return false;
            var control = _control[0];
            return control.State == 2
                && control.OwnerId == _ownerId
                && control.Generation == _generation
                && control.ActiveLeaseId == _leaseId
                && control.NextCommandSequence == _nextCommandSequence
                && control.CommandSequenceExhausted == _commandSequenceExhausted;
        }
    }

    internal struct NativeCommandMergeControlV1
    {
        internal byte State;
        internal ulong Epoch;
    }

    /// <summary>
    /// Borrowed view valid only while its owner storage is alive. Reset invalidates copied views;
    /// calling a copied view after successful owner disposal is outside the ABI lifetime contract.
    /// </summary>
    public readonly struct NativeMergedCommandViewV1
    {
        private readonly NativeArray<NativeCommandMergeControlV1>.ReadOnly _control;
        private readonly ulong _epoch;
        private readonly NativeArray<NativeCommandRecordV1>.ReadOnly _records;
        private readonly NativeArray<byte>.ReadOnly _payload;

        internal NativeMergedCommandViewV1(
            NativeArray<NativeCommandMergeControlV1>.ReadOnly control,
            ulong epoch,
            NativeArray<NativeCommandRecordV1>.ReadOnly records,
            uint count,
            NativeArray<byte>.ReadOnly payload,
            uint payloadCount)
        {
            _control = control;
            _epoch = epoch;
            _records = records;
            _payload = payload;
            Count = count;
            PayloadCount = payloadCount;
        }

        public uint Count { get; }
        public uint PayloadCount { get; }

        public BurstContextResult TryGetRecord(uint index, out NativeCommandRecordV1 record)
        {
            record = default;
            if (!IsLive() || !_records.IsCreated || index >= Count || index >= (uint)_records.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            record = _records[(int)index];
            return BurstContextResult.Success;
        }

        public BurstContextResult TryGetPayloadByte(uint index, out byte value)
        {
            value = 0;
            if (!IsLive() || !_payload.IsCreated || index >= PayloadCount || index >= (uint)_payload.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            value = _payload[(int)index];
            return BurstContextResult.Success;
        }

        private bool IsLive()
        {
            if (!_control.IsCreated || _control.Length != 1) return false;
            var control = _control[0];
            return control.State == 2 && control.Epoch == _epoch;
        }
    }

    public readonly struct NativeCommandAsyncLeaseV1
    {
        internal NativeCommandAsyncLeaseV1(ulong ownerId, uint generation, ulong leaseId, NativeCommandAsyncViewV1 view)
        {
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
            View = view;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public NativeCommandAsyncViewV1 View { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && LeaseId != 0;
    }

    internal struct NativeOperationRecordV1
    {
        internal OperationId OperationId;
        internal NativeOperationStateV1 State;
        internal CommandType CancelCommandType;
        internal uint FaultCancelPayloadOffset;
        internal uint FaultCancelPayloadSize;
    }

    internal struct NativePendingCompletionRecordV1
    {
        internal OperationId OperationId;
        internal CompletionOutcome Outcome;
        internal CompletionPayloadType PayloadType;
        internal uint PayloadOffset;
        internal uint PayloadSize;
        internal ulong SourceId;
        internal ulong SourceSequence;
        internal Revision SnapshotRevision;
        internal byte IsOccupied;
    }

    internal struct NativeCompletionHighWaterV1
    {
        internal ulong SourceId;
        internal ulong Sequence;
    }

    internal struct NativeCommandAsyncControlV1
    {
        internal byte State;
        internal ulong OwnerId;
        internal uint Generation;
        internal ulong ActiveLeaseId;
        internal ulong NextLeaseId;
        internal byte DependencyRegistered;
        internal ulong NextOperationSequence;
        internal byte OperationSequenceExhausted;
        internal ulong NextCommandSequence;
        internal byte CommandSequenceExhausted;
        internal uint OperationCount;
        internal uint OperationPayloadCount;
        internal uint PendingCount;
        internal uint CompletionPayloadCount;
        internal uint HighWaterCount;
        internal uint DiagnosticCount;
        internal ulong CompletionEpoch;
        internal byte ExecutionSealed;
        internal uint ExecuteCount;
        internal uint CancelCount;
        internal uint CommandPayloadCount;
    }
}
