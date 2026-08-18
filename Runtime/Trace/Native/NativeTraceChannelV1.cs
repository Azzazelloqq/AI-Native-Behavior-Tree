using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AIBT
{
    public struct NativeTraceWriterV1
    {
        [NativeDisableParallelForRestriction] private NativeArray<NativeTraceRecordV1> _records;
        [NativeDisableParallelForRestriction] private NativeArray<byte> _payload;
        [NativeDisableParallelForRestriction] private NativeArray<ulong> _seenSequences;
        [NativeDisableParallelForRestriction] private NativeArray<uint> _control;
        [NativeDisableParallelForRestriction] private NativeArray<ulong> _droppedCount;
        [NativeDisableParallelForRestriction] private NativeArray<NativeTraceRecordV1> _firstDropped;
        [NativeDisableParallelForRestriction] private NativeList<int> _appendLock;
        private readonly NativeTraceLevelV1 _level;
        private readonly ulong _treeInstanceId;
        private readonly uint _workerOrdinal;
        private readonly uint _ordinaryCapacity;
        private readonly uint _maximumPayloadBytes;

        internal NativeTraceWriterV1(
            NativeArray<NativeTraceRecordV1> records,
            NativeArray<byte> payload,
            NativeArray<ulong> seenSequences,
            NativeArray<uint> control,
            NativeArray<ulong> droppedCount,
            NativeArray<NativeTraceRecordV1> firstDropped,
            NativeList<int> appendLock,
            NativeTraceLevelV1 level,
            ulong treeInstanceId,
            uint workerOrdinal,
            uint ordinaryCapacity,
            uint maximumPayloadBytes)
        {
            _records = records;
            _payload = payload;
            _seenSequences = seenSequences;
            _control = control;
            _droppedCount = droppedCount;
            _firstDropped = firstDropped;
            _appendLock = appendLock;
            _level = level;
            _treeInstanceId = treeInstanceId;
            _workerOrdinal = workerOrdinal;
            _ordinaryCapacity = ordinaryCapacity;
            _maximumPayloadBytes = maximumPayloadBytes;
        }

        public NativeTraceAppendResultV1 TryAppend(in NativeTraceRecordV1 record)
            => TryAppend(record, default, 0);

        public NativeTraceAppendResultV1 TryAppend(
            in NativeTraceRecordV1 record,
            NativeArray<byte>.ReadOnly payload,
            uint payloadLength)
        {
            // Off is deliberately the first branch: it observes no counters or channel state.
            if (_level == NativeTraceLevelV1.Off)
            {
                return NativeTraceAppendResultV1.Filtered;
            }

            AcquireLock();

            try
            {
                return TryAppendLocked(record, payload, payloadLength);
            }
            finally
            {
                Interlocked.Exchange(ref _appendLock.ElementAt(0), 0);
            }
        }

        private NativeTraceAppendResultV1 TryAppendLocked(
            in NativeTraceRecordV1 record,
            NativeArray<byte>.ReadOnly payload,
            uint payloadLength)
        {

            if (_control[2] != 0)
            {
                return NativeTraceAppendResultV1.ChannelFaulted;
            }

            if (!ValidateRecord(record, payload, payloadLength))
            {
                return NativeTraceAppendResultV1.InvalidRecord;
            }

            if (!Includes(_level, record.Kind))
            {
                return NativeTraceAppendResultV1.Filtered;
            }

            var seenCount = _control[1];
            for (var index = 0u; index < seenCount; index++)
            {
                if (_seenSequences[(int)index] == record.Sequence)
                {
                    _control[2] = 1;
                    return NativeTraceAppendResultV1.DuplicateSequence;
                }
            }

            if (seenCount >= _seenSequences.Length)
            {
                _control[2] = 1;
                return NativeTraceAppendResultV1.ChannelFaulted;
            }

            _seenSequences[(int)seenCount] = record.Sequence;
            _control[1] = seenCount + 1;

            if (payloadLength > _maximumPayloadBytes)
            {
                return RecordDrop(record)
                    ? NativeTraceAppendResultV1.Dropped
                    : NativeTraceAppendResultV1.ChannelFaulted;
            }

            var count = _control[0];
            if (count < _ordinaryCapacity)
            {
                Insert(record, payload, payloadLength, count);
                _control[0] = count + 1;
                return NativeTraceAppendResultV1.Written;
            }

            if (_ordinaryCapacity == 0 || record.Sequence > _records[(int)_ordinaryCapacity - 1].Sequence)
            {
                return RecordDrop(record)
                    ? NativeTraceAppendResultV1.Dropped
                    : NativeTraceAppendResultV1.ChannelFaulted;
            }

            var ejected = _records[(int)_ordinaryCapacity - 1];
            if (!RecordDrop(ejected)) return NativeTraceAppendResultV1.ChannelFaulted;
            Insert(record, payload, payloadLength, _ordinaryCapacity - 1);
            return NativeTraceAppendResultV1.Written;
        }

        private void AcquireLock()
        {
            while (Interlocked.CompareExchange(ref _appendLock.ElementAt(0), 1, 0) != 0)
            {
            }
        }

        private bool ValidateRecord(
            in NativeTraceRecordV1 record,
            NativeArray<byte>.ReadOnly payload,
            uint payloadLength)
            => record.HasValidHeader
                && record.Kind != NativeTraceEventKindV1.TraceDroppedSummary
                && record.TreeInstanceId == _treeInstanceId
                && record.WorkerOrdinal == _workerOrdinal
                && record.PayloadOffset == 0
                && record.PayloadLength == 0
                && (payloadLength == 0 || payload.IsCreated && payloadLength <= payload.Length);

        private void Insert(
            in NativeTraceRecordV1 record,
            NativeArray<byte>.ReadOnly sourcePayload,
            uint payloadLength,
            uint lastOccupiedOrFree)
        {
            var insert = lastOccupiedOrFree;
            while (insert > 0 && record.Sequence < _records[(int)insert - 1].Sequence)
            {
                var moved = _records[(int)insert - 1];
                CopyPayloadSlot(insert - 1, insert, moved.PayloadLength);
                moved.PayloadOffset = insert * _maximumPayloadBytes;
                _records[(int)insert] = moved;
                insert--;
            }

            var stored = record;
            stored.PayloadOffset = insert * _maximumPayloadBytes;
            stored.PayloadLength = payloadLength;
            _records[(int)insert] = stored;
            var destinationOffset = stored.PayloadOffset;
            for (var index = 0u; index < payloadLength; index++)
            {
                _payload[(int)(destinationOffset + index)] = sourcePayload[(int)index];
            }
        }

        private void CopyPayloadSlot(uint sourceSlot, uint destinationSlot, uint length)
        {
            var sourceOffset = sourceSlot * _maximumPayloadBytes;
            var destinationOffset = destinationSlot * _maximumPayloadBytes;
            for (var index = 0u; index < length; index++)
            {
                _payload[(int)(destinationOffset + index)] = _payload[(int)(sourceOffset + index)];
            }
        }

        private bool RecordDrop(in NativeTraceRecordV1 record)
        {
            if (_droppedCount[0] == ulong.MaxValue)
            {
                _control[2] = 1;
                return false;
            }

            _droppedCount[0]++;
            if (_control[3] == 0 || record.Sequence < _firstDropped[0].Sequence)
            {
                _firstDropped[0] = record;
                _control[3] = 1;
            }

            return true;
        }

        internal static bool Includes(NativeTraceLevelV1 level, NativeTraceEventKindV1 kind)
        {
            if (level == NativeTraceLevelV1.Detailed) return kind != NativeTraceEventKindV1.TraceDroppedSummary;
            if (level == NativeTraceLevelV1.Errors) return kind == NativeTraceEventKindV1.DiagnosticRaised;
            if (level != NativeTraceLevelV1.Lifecycle) return false;
            switch (kind)
            {
                case NativeTraceEventKindV1.UpdateStarted:
                case NativeTraceEventKindV1.UpdateCompleted:
                case NativeTraceEventKindV1.NodeEntered:
                case NativeTraceEventKindV1.NodeTicked:
                case NativeTraceEventKindV1.NodeAbortStarted:
                case NativeTraceEventKindV1.NodeExited:
                case NativeTraceEventKindV1.CompletionConsumed:
                case NativeTraceEventKindV1.CompletionDiscarded:
                case NativeTraceEventKindV1.DiagnosticRaised:
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class NativeTraceChannelOwnerV1
    {
        private NativeArray<NativeTraceRecordV1> _records;
        private NativeArray<byte> _payload;
        private NativeArray<ulong> _seenSequences;
        private NativeArray<uint> _control;
        private NativeArray<ulong> _droppedCount;
        private NativeArray<NativeTraceRecordV1> _firstDropped;
        private NativeList<int> _appendLock;
        private NativeLeaseTokenV1 _activeLease;
        private JobHandle _dependency;
        private ulong _nextLeaseId;
        private ulong _treeInstanceId;
        private uint _workerOrdinal;
        private NativeTraceLevelV1 _level;

        private NativeTraceChannelOwnerV1() { }

        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public NativeOwnerStateV1 State { get; private set; }
        public NativeTraceChannelCapacityV1 Capacity { get; private set; }
        public NativeTraceLevelV1 Level => _level;

        public static bool TryCreate(
            NativeTraceChannelCapacityV1 capacity,
            NativeTraceLevelV1 level,
            TreeInstanceId treeInstanceId,
            uint workerOrdinal,
            Allocator allocator,
            out NativeTraceChannelOwnerV1 owner,
            out NativeTraceChannelFailureV1 failure)
            => TryCreate(capacity, level, treeInstanceId, workerOrdinal, allocator, -1, out owner, out failure);

        internal static bool TryCreate(
            NativeTraceChannelCapacityV1 capacity,
            NativeTraceLevelV1 level,
            TreeInstanceId treeInstanceId,
            uint workerOrdinal,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeTraceChannelOwnerV1 owner,
            out NativeTraceChannelFailureV1 failure)
        {
            owner = null;
            if (allocator != Allocator.Persistent)
            {
                failure = new NativeTraceChannelFailureV1(NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid);
                return false;
            }

            if (!treeInstanceId.IsValid || (byte)level > (byte)NativeTraceLevelV1.Detailed)
            {
                failure = CapacityFailure(NativeDiagnosticResourceKindV1.TraceRecords, capacity.RecordCapacity, capacity.RecordCapacity);
                return false;
            }

            var active = level != NativeTraceLevelV1.Off;
            ulong requiredPayload;
            try
            {
                requiredPayload = checked((ulong)capacity.OrdinaryRecordCapacity * capacity.MaximumPayloadBytes);
            }
            catch (OverflowException)
            {
                failure = OverflowFailure(NativeDiagnosticResourceKindV1.TracePayload, capacity.PayloadCapacity);
                return false;
            }

            if (capacity.RecordCapacity > int.MaxValue
                || capacity.PayloadCapacity > int.MaxValue
                || capacity.EmissionCapacity > int.MaxValue
                || requiredPayload > int.MaxValue)
            {
                var payloadOverflow = capacity.PayloadCapacity > int.MaxValue || requiredPayload > int.MaxValue;
                failure = OverflowFailure(
                    payloadOverflow
                        ? NativeDiagnosticResourceKindV1.TracePayload
                        : NativeDiagnosticResourceKindV1.TraceRecords,
                    payloadOverflow
                        ? Math.Max(requiredPayload, capacity.PayloadCapacity)
                        : Math.Max(capacity.RecordCapacity, capacity.EmissionCapacity));
                return false;
            }

            if (requiredPayload > capacity.PayloadCapacity)
            {
                failure = CapacityFailure(
                    NativeDiagnosticResourceKindV1.TracePayload,
                    requiredPayload,
                    capacity.PayloadCapacity);
                return false;
            }

            if (active && (capacity.RecordCapacity == 0 || capacity.EmissionCapacity == 0))
            {
                failure = CapacityFailure(
                    NativeDiagnosticResourceKindV1.TraceRecords,
                    1,
                    capacity.RecordCapacity == 0 ? 0 : capacity.EmissionCapacity);
                return false;
            }

            var records = default(NativeArray<NativeTraceRecordV1>);
            var payload = default(NativeArray<byte>);
            var seen = default(NativeArray<ulong>);
            var control = default(NativeArray<uint>);
            var dropped = default(NativeArray<ulong>);
            var firstDropped = default(NativeArray<NativeTraceRecordV1>);
            var appendLock = default(NativeList<int>);
            var allocationCount = 0;
            var resource = NativeDiagnosticResourceKindV1.TraceRecords;
            try
            {
                records = Allocate<NativeTraceRecordV1>(capacity.RecordCapacity, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                resource = NativeDiagnosticResourceKindV1.TracePayload;
                payload = Allocate<byte>(capacity.PayloadCapacity, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                seen = Allocate<ulong>(capacity.EmissionCapacity, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                control = Allocate<uint>(4, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                dropped = Allocate<ulong>(1, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                firstDropped = Allocate<NativeTraceRecordV1>(1, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                if (failAfterSuccessfulAllocations >= 0 && allocationCount == failAfterSuccessfulAllocations) throw new InvalidOperationException();
                appendLock = new NativeList<int>(1, allocator);
                appendLock.Add(0);
                allocationCount++;
                if (!NativeOwnerIdentityV1.TryNext(out var ownerId)) throw new OverflowException();

                owner = new NativeTraceChannelOwnerV1
                {
                    OwnerId = ownerId,
                    Generation = 1,
                    State = NativeOwnerStateV1.Initialized,
                    Capacity = capacity,
                    _treeInstanceId = treeInstanceId.Value,
                    _workerOrdinal = workerOrdinal,
                    _level = level,
                    _records = records,
                    _payload = payload,
                    _seenSequences = seen,
                    _control = control,
                    _droppedCount = dropped,
                    _firstDropped = firstDropped,
                    _appendLock = appendLock,
                };
                failure = default;
                return true;
            }
            catch (OverflowException)
            {
                DisposeReverse(ref appendLock, ref firstDropped, ref dropped, ref control, ref seen, ref payload, ref records);
                failure = OverflowFailure(NativeDiagnosticResourceKindV1.LeaseCounter, ulong.MaxValue);
                return false;
            }
            catch (Exception)
            {
                DisposeReverse(ref appendLock, ref firstDropped, ref dropped, ref control, ref seen, ref payload, ref records);
                failure = CapacityFailure(resource, (ulong)allocationCount + 1, (ulong)allocationCount);
                return false;
            }
        }

        public bool TryAcquireWriter(out NativeTraceChannelLeaseV1 lease, out NativeTraceChannelFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing && _activeLease.IsValid)
            {
                lease = default;
                failure = LiveFailure(_activeLease.LeaseId);
                return false;
            }

            if (State != NativeOwnerStateV1.Initialized)
            {
                lease = default;
                failure = LifetimeFailure(0);
                return false;
            }

            if (_nextLeaseId == ulong.MaxValue)
            {
                lease = default;
                failure = OverflowFailure(NativeDiagnosticResourceKindV1.LeaseCounter, _nextLeaseId);
                return false;
            }

            _activeLease = new NativeLeaseTokenV1(OwnerId, Generation, ++_nextLeaseId);
            _dependency = default;
            State = NativeOwnerStateV1.Executing;
            lease = new NativeTraceChannelLeaseV1(
                this,
                _activeLease,
                new NativeTraceWriterV1(
                    _records, _payload, _seenSequences, _control, _droppedCount, _firstDropped, _appendLock,
                    _level, _treeInstanceId, _workerOrdinal, Capacity.OrdinaryRecordCapacity, Capacity.MaximumPayloadBytes));
            failure = default;
            return true;
        }

        public bool TryRegisterDependency(NativeTraceChannelLeaseV1 lease, JobHandle dependency, out NativeTraceChannelFailureV1 failure)
        {
            if (!IsActive(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }

            _dependency = JobHandle.CombineDependencies(_dependency, dependency);
            failure = default;
            return true;
        }

        public bool TryReleaseWriter(NativeTraceChannelLeaseV1 lease, out NativeTraceChannelFailureV1 failure)
        {
            if (!IsActive(lease))
            {
                failure = LifetimeFailure(lease.Token.LeaseId);
                return false;
            }

            if (!_dependency.IsCompleted)
            {
                failure = LiveFailure(lease.Token.LeaseId);
                return false;
            }

            _dependency.Complete();

            _activeLease = default;
            _dependency = default;
            State = NativeOwnerStateV1.Initialized;
            failure = default;
            return true;
        }

        public bool TryGetSnapshot(out NativeTraceChannelSnapshotV1 snapshot, out NativeTraceChannelFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized)
            {
                snapshot = default;
                failure = State == NativeOwnerStateV1.Executing ? LiveFailure(_activeLease.LeaseId) : LifetimeFailure(0);
                return false;
            }

            var count = _control[0];
            if (_droppedCount[0] != 0)
            {
                var summary = _firstDropped[0];
                summary.Kind = NativeTraceEventKindV1.TraceDroppedSummary;
                summary.OptionalFields = NativeTraceOptionalFieldsV1.None;
                summary.RuntimeNodeIndex = CompiledIndex.Invalid;
                summary.DebugIdentityIndex = CompiledIndex.Invalid;
                summary.SourceNodeIndex = CompiledIndex.Invalid;
                summary.PayloadOffset = 0;
                summary.PayloadLength = 0;
                summary.DroppedCount = _droppedCount[0];
                _records[(int)count] = summary;
                count++;
            }

            snapshot = new NativeTraceChannelSnapshotV1(
                _records.AsReadOnly(), _payload.AsReadOnly(), count, _droppedCount[0], _control[2] != 0);
            failure = default;
            return true;
        }

        public bool TryReset(out NativeTraceChannelFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized)
            {
                failure = State == NativeOwnerStateV1.Executing ? LiveFailure(_activeLease.LeaseId) : LifetimeFailure(0);
                return false;
            }

            if (Generation == uint.MaxValue)
            {
                failure = OverflowFailure(NativeDiagnosticResourceKindV1.LeaseCounter, Generation);
                return false;
            }

            Generation++;
            Clear(_records);
            Clear(_payload);
            Clear(_seenSequences);
            Clear(_control);
            Clear(_droppedCount);
            Clear(_firstDropped);
            failure = default;
            return true;
        }

        internal bool TrySetDroppedCountForVerification(ulong value)
        {
            if (State != NativeOwnerStateV1.Initialized) return false;
            _droppedCount[0] = value;
            return true;
        }

        public bool TryDispose(out NativeTraceChannelFailureV1 failure)
        {
            if (State == NativeOwnerStateV1.Executing)
            {
                failure = LiveFailure(_activeLease.LeaseId);
                return false;
            }

            if (State != NativeOwnerStateV1.Initialized)
            {
                failure = LifetimeFailure(0);
                return false;
            }

            DisposeReverse(ref _appendLock, ref _firstDropped, ref _droppedCount, ref _control, ref _seenSequences, ref _payload, ref _records);
            State = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        private bool IsActive(NativeTraceChannelLeaseV1 lease)
            => ReferenceEquals(lease.Owner, this) && lease.Token == _activeLease && State == NativeOwnerStateV1.Executing;

        private NativeTraceChannelFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeTraceChannelFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                ownerId: OwnerId, generation: Generation, leaseId: leaseId);

        private NativeTraceChannelFailureV1 LiveFailure(ulong leaseId)
            => new NativeTraceChannelFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                ownerId: OwnerId, generation: Generation, leaseId: leaseId);

        private static NativeTraceChannelFailureV1 CapacityFailure(NativeDiagnosticResourceKindV1 resource, ulong requested, ulong capacity)
            => new NativeTraceChannelFailureV1(NativeRuntimeDiagnosticCodeV1.NativeTraceCapacityExceeded, resource, requested, capacity);

        private static NativeTraceChannelFailureV1 OverflowFailure(NativeDiagnosticResourceKindV1 resource, ulong requested)
            => new NativeTraceChannelFailureV1(NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow, resource, requested, int.MaxValue);

        private static NativeArray<T> Allocate<T>(uint count, Allocator allocator, int failAfter, ref int allocationCount) where T : struct
        {
            if (failAfter >= 0 && allocationCount == failAfter) throw new InvalidOperationException();
            var result = new NativeArray<T>((int)count, allocator, NativeArrayOptions.ClearMemory);
            allocationCount++;
            return result;
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            for (var index = 0; index < array.Length; index++) array[index] = default;
        }

        private static void DisposeReverse(
            ref NativeList<int> appendLock,
            ref NativeArray<NativeTraceRecordV1> firstDropped,
            ref NativeArray<ulong> dropped,
            ref NativeArray<uint> control,
            ref NativeArray<ulong> seen,
            ref NativeArray<byte> payload,
            ref NativeArray<NativeTraceRecordV1> records)
        {
            if (appendLock.IsCreated)
            {
                appendLock.Dispose();
                appendLock = default;
            }
            Dispose(ref firstDropped);
            Dispose(ref dropped);
            Dispose(ref control);
            Dispose(ref seen);
            Dispose(ref payload);
            Dispose(ref records);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated) return;
            array.Dispose();
            array = default;
        }
    }

    public readonly struct NativeTraceChannelLeaseV1
    {
        internal NativeTraceChannelLeaseV1(NativeTraceChannelOwnerV1 owner, NativeLeaseTokenV1 token, NativeTraceWriterV1 writer)
        {
            Owner = owner;
            Token = token;
            Writer = writer;
        }

        internal NativeTraceChannelOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeTraceWriterV1 Writer { get; }
    }

    public static class NativeTraceMergeV1
    {
        public static bool TryMerge(
            in NativeTraceChannelSnapshotV1 first,
            in NativeTraceChannelSnapshotV1 second,
            NativeArray<NativeTraceRecordV1> destinationRecords,
            NativeArray<byte> destinationPayload,
            out uint mergedRecordCount,
            out uint mergedPayloadCount)
        {
            mergedRecordCount = 0;
            mergedPayloadCount = 0;
            if (first.IsFaulted || second.IsFaulted
                || !ValidateSource(first.Records, first.RecordCount, first.Payload, (uint)first.Payload.Length, out var firstPayload)
                || !ValidateSource(second.Records, second.RecordCount, second.Payload, (uint)second.Payload.Length, out var secondPayload)
                || (ulong)first.RecordCount + second.RecordCount > (uint)destinationRecords.Length
                || firstPayload + secondPayload > (uint)destinationPayload.Length
                || HasDuplicateAcross(first.Records, first.RecordCount, second.Records, second.RecordCount))
            {
                return false;
            }

            InsertSource(first.Records, first.RecordCount, first.Payload, destinationRecords, destinationPayload, ref mergedRecordCount, ref mergedPayloadCount);
            InsertSource(second.Records, second.RecordCount, second.Payload, destinationRecords, destinationPayload, ref mergedRecordCount, ref mergedPayloadCount);
            return true;
        }

        public static bool TryMerge(
            NativeArray<NativeTraceRecordV1>.ReadOnly source,
            uint sourceCount,
            NativeArray<NativeTraceRecordV1> destination,
            out uint mergedCount)
        {
            mergedCount = 0;
            if (sourceCount > source.Length || sourceCount > destination.Length) return false;
            for (var index = 0u; index < sourceCount; index++)
            {
                var record = source[(int)index];
                if (!record.HasValidHeader || record.PayloadOffset != 0 || record.PayloadLength != 0) return false;
                for (var previous = 0u; previous < index; previous++)
                {
                    var candidate = source[(int)previous];
                    if (candidate.TreeInstanceId == record.TreeInstanceId && candidate.Sequence == record.Sequence) return false;
                }
            }

            for (var index = 0u; index < sourceCount; index++)
            {
                var record = source[(int)index];
                var insert = mergedCount;
                while (insert > 0 && Compare(record, destination[(int)insert - 1]) < 0)
                {
                    destination[(int)insert] = destination[(int)insert - 1];
                    insert--;
                }

                destination[(int)insert] = record;
                mergedCount++;
            }

            return true;
        }

        private static bool ValidateSource(
            NativeArray<NativeTraceRecordV1>.ReadOnly records,
            uint recordCount,
            NativeArray<byte>.ReadOnly payload,
            uint payloadCount,
            out ulong requiredPayload)
        {
            requiredPayload = 0;
            if (recordCount > records.Length || payloadCount > payload.Length) return false;
            for (var index = 0u; index < recordCount; index++)
            {
                var record = records[(int)index];
                if (!record.HasValidHeader
                    || record.Kind == NativeTraceEventKindV1.TraceDroppedSummary
                        && (record.PayloadOffset != 0 || record.PayloadLength != 0)
                    || record.Kind != NativeTraceEventKindV1.TraceDroppedSummary
                        && (ulong)record.PayloadOffset + record.PayloadLength > payloadCount)
                {
                    return false;
                }

                requiredPayload += record.PayloadLength;
                for (var previous = 0u; previous < index; previous++)
                {
                    var candidate = records[(int)previous];
                    if (candidate.TreeInstanceId == record.TreeInstanceId && candidate.Sequence == record.Sequence) return false;
                }
            }

            return true;
        }

        private static bool HasDuplicateAcross(
            NativeArray<NativeTraceRecordV1>.ReadOnly first,
            uint firstCount,
            NativeArray<NativeTraceRecordV1>.ReadOnly second,
            uint secondCount)
        {
            for (var firstIndex = 0u; firstIndex < firstCount; firstIndex++)
            {
                var left = first[(int)firstIndex];
                for (var secondIndex = 0u; secondIndex < secondCount; secondIndex++)
                {
                    var right = second[(int)secondIndex];
                    if (left.TreeInstanceId == right.TreeInstanceId && left.Sequence == right.Sequence) return true;
                }
            }

            return false;
        }

        private static void InsertSource(
            NativeArray<NativeTraceRecordV1>.ReadOnly sourceRecords,
            uint sourceRecordCount,
            NativeArray<byte>.ReadOnly sourcePayload,
            NativeArray<NativeTraceRecordV1> destinationRecords,
            NativeArray<byte> destinationPayload,
            ref uint mergedRecordCount,
            ref uint mergedPayloadCount)
        {
            for (var index = 0u; index < sourceRecordCount; index++)
            {
                var record = sourceRecords[(int)index];
                var insert = mergedRecordCount;
                while (insert > 0 && Compare(record, destinationRecords[(int)insert - 1]) < 0) insert--;
                var payloadOffset = 0u;
                if (record.PayloadLength != 0)
                {
                    for (var recordIndex = 0u; recordIndex < insert; recordIndex++)
                        payloadOffset += destinationRecords[(int)recordIndex].PayloadLength;
                    for (var payloadIndex = mergedPayloadCount; payloadIndex > payloadOffset; payloadIndex--)
                        destinationPayload[(int)(payloadIndex - 1 + record.PayloadLength)] = destinationPayload[(int)payloadIndex - 1];
                }

                for (var recordIndex = mergedRecordCount; recordIndex > insert; recordIndex--)
                {
                    var moved = destinationRecords[(int)recordIndex - 1];
                    if (moved.PayloadLength != 0) moved.PayloadOffset += record.PayloadLength;
                    destinationRecords[(int)recordIndex] = moved;
                }

                for (var payloadIndex = 0u; payloadIndex < record.PayloadLength; payloadIndex++)
                    destinationPayload[(int)(payloadOffset + payloadIndex)] = sourcePayload[(int)(record.PayloadOffset + payloadIndex)];
                record.PayloadOffset = record.PayloadLength == 0 ? 0 : payloadOffset;
                destinationRecords[(int)insert] = record;
                mergedRecordCount++;
                mergedPayloadCount += record.PayloadLength;
            }
        }

        public static bool TryMerge(
            NativeArray<NativeTraceRecordV1>.ReadOnly sourceRecords,
            uint sourceRecordCount,
            NativeArray<byte>.ReadOnly sourcePayload,
            uint sourcePayloadCount,
            NativeArray<NativeTraceRecordV1> destinationRecords,
            NativeArray<byte> destinationPayload,
            out uint mergedRecordCount,
            out uint mergedPayloadCount)
        {
            mergedRecordCount = 0;
            mergedPayloadCount = 0;
            if (sourceRecordCount > sourceRecords.Length
                || sourcePayloadCount > sourcePayload.Length
                || sourceRecordCount > destinationRecords.Length)
            {
                return false;
            }

            ulong requiredPayload = 0;
            for (var index = 0u; index < sourceRecordCount; index++)
            {
                var record = sourceRecords[(int)index];
                if (!record.HasValidHeader
                    || record.Kind == NativeTraceEventKindV1.TraceDroppedSummary
                        && (record.PayloadOffset != 0 || record.PayloadLength != 0)
                    || record.Kind != NativeTraceEventKindV1.TraceDroppedSummary
                        && ((ulong)record.PayloadOffset + record.PayloadLength > sourcePayloadCount))
                {
                    return false;
                }

                requiredPayload += record.PayloadLength;
                if (requiredPayload > (uint)destinationPayload.Length) return false;
                for (var previous = 0u; previous < index; previous++)
                {
                    var candidate = sourceRecords[(int)previous];
                    if (candidate.TreeInstanceId == record.TreeInstanceId && candidate.Sequence == record.Sequence) return false;
                }
            }

            for (var index = 0u; index < sourceRecordCount; index++)
            {
                var record = sourceRecords[(int)index];
                var insert = mergedRecordCount;
                while (insert > 0 && Compare(record, destinationRecords[(int)insert - 1]) < 0) insert--;

                var payloadOffset = 0u;
                if (record.PayloadLength != 0)
                {
                    for (var recordIndex = 0u; recordIndex < insert; recordIndex++)
                    {
                        payloadOffset += destinationRecords[(int)recordIndex].PayloadLength;
                    }
                    for (var payloadIndex = mergedPayloadCount; payloadIndex > payloadOffset; payloadIndex--)
                    {
                        destinationPayload[(int)(payloadIndex - 1 + record.PayloadLength)] = destinationPayload[(int)payloadIndex - 1];
                    }
                }

                for (var recordIndex = mergedRecordCount; recordIndex > insert; recordIndex--)
                {
                    var moved = destinationRecords[(int)recordIndex - 1];
                    if (moved.PayloadLength != 0) moved.PayloadOffset += record.PayloadLength;
                    destinationRecords[(int)recordIndex] = moved;
                }

                for (var payloadIndex = 0u; payloadIndex < record.PayloadLength; payloadIndex++)
                {
                    destinationPayload[(int)(payloadOffset + payloadIndex)] = sourcePayload[(int)(record.PayloadOffset + payloadIndex)];
                }

                record.PayloadOffset = record.PayloadLength == 0 ? 0 : payloadOffset;
                destinationRecords[(int)insert] = record;
                mergedRecordCount++;
                mergedPayloadCount += record.PayloadLength;
            }

            return true;
        }

        private static int Compare(in NativeTraceRecordV1 left, in NativeTraceRecordV1 right)
        {
            var result = ((byte)left.Phase).CompareTo((byte)right.Phase);
            if (result != 0) return result;
            result = left.TreeInstanceId.CompareTo(right.TreeInstanceId);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }
    }
}
