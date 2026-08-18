using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AIBT
{
    public struct NativeDiagnosticWriterV1
    {
        [NativeDisableParallelForRestriction] private NativeArray<NativeDiagnosticRecordV1> _records;
        [NativeDisableParallelForRestriction] private NativeArray<NativeDiagnosticLocationV1> _relatedLocations;
        [NativeDisableParallelForRestriction] private NativeArray<NativeDiagnosticRecordV1> _rejection;
        [NativeDisableParallelForRestriction] private NativeArray<uint> _control;
        [NativeDisableParallelForRestriction] private NativeList<int> _appendLock;
        private readonly ulong _treeInstanceId;
        private readonly uint _workerOrdinal;

        internal NativeDiagnosticWriterV1(
            NativeArray<NativeDiagnosticRecordV1> records,
            NativeArray<NativeDiagnosticLocationV1> relatedLocations,
            NativeArray<NativeDiagnosticRecordV1> rejection,
            NativeArray<uint> control,
            NativeList<int> appendLock,
            ulong treeInstanceId,
            uint workerOrdinal)
        {
            _records = records;
            _relatedLocations = relatedLocations;
            _rejection = rejection;
            _control = control;
            _appendLock = appendLock;
            _treeInstanceId = treeInstanceId;
            _workerOrdinal = workerOrdinal;
        }

        public NativeDiagnosticAppendResultV1 TryAppend(in NativeDiagnosticRecordV1 record)
            => TryAppend(record, default, 0);

        public NativeDiagnosticAppendResultV1 TryAppend(
            in NativeDiagnosticRecordV1 record,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly relatedLocations,
            uint relatedLocationCount)
        {
            AcquireLock();

            try
            {
                return TryAppendLocked(record, relatedLocations, relatedLocationCount);
            }
            finally
            {
                Interlocked.Exchange(ref _appendLock.ElementAt(0), 0);
            }
        }

        private NativeDiagnosticAppendResultV1 TryAppendLocked(
            in NativeDiagnosticRecordV1 record,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly relatedLocations,
            uint relatedLocationCount)
        {
            if (!ValidateRecord(record, relatedLocations, relatedLocationCount))
            {
                return NativeDiagnosticAppendResultV1.InvalidRecord;
            }

            var count = _control[0];
            var locationCount = _control[1];
            var insert = FindInsert(record, count);
            var overflowed = false;
            while (count >= _records.Length || (ulong)locationCount + relatedLocationCount > (uint)_relatedLocations.Length)
            {
                var recordOverflow = count >= _records.Length;
                var resource = recordOverflow
                    ? NativeDiagnosticResourceKindV1.DiagnosticRecords
                    : NativeDiagnosticResourceKindV1.DiagnosticLocations;
                var requested = recordOverflow
                    ? (ulong)count + 1
                    : (ulong)locationCount + relatedLocationCount;
                var capacity = recordOverflow ? (uint)_records.Length : (uint)_relatedLocations.Length;

                if (count == 0 || insert >= count)
                {
                    Fault(record, resource, requested, capacity);
                    return NativeDiagnosticAppendResultV1.ChannelFaulted;
                }

                var ejected = _records[(int)count - 1];
                Fault(ejected, resource, requested, capacity);
                locationCount -= ejected.RelatedLocationCount;
                count--;
                if (insert > count) insert = count;
                overflowed = true;
            }

            Insert(record, relatedLocations, relatedLocationCount, insert, count, locationCount);
            _control[0] = count + 1;
            _control[1] = locationCount + relatedLocationCount;
            return overflowed || _control[2] != 0
                ? NativeDiagnosticAppendResultV1.ChannelFaulted
                : NativeDiagnosticAppendResultV1.Written;
        }

        private uint FindInsert(in NativeDiagnosticRecordV1 record, uint count)
        {
            var insert = 0u;
            while (insert < count && NativeDiagnosticOrderV1.Compare(_records[(int)insert], record) <= 0) insert++;
            return insert;
        }

        private void Insert(
            in NativeDiagnosticRecordV1 record,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly sourceLocations,
            uint sourceLocationCount,
            uint insert,
            uint count,
            uint locationCount)
        {
            var locationOffset = insert < count ? _records[(int)insert].RelatedLocationOffset : locationCount;
            for (var index = locationCount; index > locationOffset; index--)
            {
                _relatedLocations[(int)(index - 1 + sourceLocationCount)] = _relatedLocations[(int)index - 1];
            }

            for (var index = count; index > insert; index--)
            {
                var moved = _records[(int)index - 1];
                moved.RelatedLocationOffset += sourceLocationCount;
                _records[(int)index] = moved;
            }

            for (var index = 0u; index < sourceLocationCount; index++)
            {
                _relatedLocations[(int)(locationOffset + index)] = sourceLocations[(int)index];
            }

            var stored = record;
            stored.RelatedLocationOffset = locationOffset;
            stored.RelatedLocationCount = sourceLocationCount;
            _records[(int)insert] = stored;
        }

        private void AcquireLock()
        {
            while (Interlocked.CompareExchange(ref _appendLock.ElementAt(0), 1, 0) != 0)
            {
            }
        }

        private bool ValidateRecord(
            in NativeDiagnosticRecordV1 record,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly relatedLocations,
            uint relatedLocationCount)
        {
            if (!record.HasValidHeader
                || record.WorkerOrdinal != _workerOrdinal
                || (record.PrimaryLocation.Flags & NativeDiagnosticLocationFlagsV1.TreeInstance) != 0
                    && record.PrimaryLocation.TreeInstanceId != _treeInstanceId
                || relatedLocationCount != 0 && (!relatedLocations.IsCreated || relatedLocationCount > relatedLocations.Length)
                || record.RelatedLocationOffset != 0
                || record.RelatedLocationCount != 0
                || !NativeDiagnosticContractV1.ValidateFields(record))
            {
                return false;
            }

            for (var index = 0; index < relatedLocationCount; index++)
            {
                if (!relatedLocations[index].IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        private void Fault(
            in NativeDiagnosticRecordV1 attempted,
            NativeDiagnosticResourceKindV1 resource,
            ulong requested,
            uint capacity)
        {
            var rejection = attempted;
            rejection.CodeNumber = (ushort)NativeRuntimeDiagnosticCodeV1.NativeDiagnosticCapacityExceeded;
            rejection.Severity = DiagnosticSeverity.Error;
            rejection.RelatedLocationOffset = 0;
            rejection.RelatedLocationCount = 0;
            rejection.FieldCount = 3;
            rejection.Field0 = new NativeDiagnosticFieldPairV1(
                NativeDiagnosticFieldIdV1.ResourceKind,
                NativeDiagnosticValueKindV1.Enum,
                (ulong)resource);
            rejection.Field1 = new NativeDiagnosticFieldPairV1(
                NativeDiagnosticFieldIdV1.Requested,
                NativeDiagnosticValueKindV1.Unsigned,
                requested);
            rejection.Field2 = new NativeDiagnosticFieldPairV1(
                NativeDiagnosticFieldIdV1.Capacity,
                NativeDiagnosticValueKindV1.Unsigned,
                capacity);
            rejection.Field3 = default;
            rejection.Field4 = default;
            rejection.Field5 = default;
            rejection.Field6 = default;
            rejection.Field7 = default;

            if (_control[2] == 0 || NativeDiagnosticOrderV1.CompareRejection(rejection, _rejection[0]) < 0)
            {
                _rejection[0] = rejection;
            }

            _control[2] = 1;
        }
    }

    public sealed class NativeDiagnosticChannelOwnerV1
    {
        private NativeArray<NativeDiagnosticRecordV1> _records;
        private NativeArray<NativeDiagnosticLocationV1> _relatedLocations;
        private NativeArray<NativeDiagnosticRecordV1> _rejection;
        private NativeArray<uint> _control;
        private NativeList<int> _appendLock;
        private NativeLeaseTokenV1 _activeLease;
        private JobHandle _dependency;
        private ulong _nextLeaseId;
        private ulong _treeInstanceId;
        private uint _workerOrdinal;

        private NativeDiagnosticChannelOwnerV1()
        {
        }

        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public NativeOwnerStateV1 State { get; private set; }
        public NativeDiagnosticChannelCapacityV1 Capacity { get; private set; }

        public static bool TryCreate(
            NativeDiagnosticChannelCapacityV1 capacity,
            TreeInstanceId treeInstanceId,
            uint workerOrdinal,
            Allocator allocator,
            out NativeDiagnosticChannelOwnerV1 owner,
            out NativeDiagnosticChannelFailureV1 failure)
            => TryCreate(capacity, treeInstanceId, workerOrdinal, allocator, -1, out owner, out failure);

        internal static bool TryCreate(
            NativeDiagnosticChannelCapacityV1 capacity,
            TreeInstanceId treeInstanceId,
            uint workerOrdinal,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeDiagnosticChannelOwnerV1 owner,
            out NativeDiagnosticChannelFailureV1 failure)
        {
            owner = null;
            if (allocator != Allocator.Persistent)
            {
                failure = new NativeDiagnosticChannelFailureV1(NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid);
                return false;
            }

            if (!treeInstanceId.IsValid
                || capacity.RecordCapacity > int.MaxValue
                || capacity.RelatedLocationCapacity > int.MaxValue)
            {
                failure = new NativeDiagnosticChannelFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeDiagnosticResourceKindV1.DiagnosticRecords,
                    capacity.RecordCapacity,
                    int.MaxValue);
                return false;
            }

            var records = default(NativeArray<NativeDiagnosticRecordV1>);
            var locations = default(NativeArray<NativeDiagnosticLocationV1>);
            var rejection = default(NativeArray<NativeDiagnosticRecordV1>);
            var control = default(NativeArray<uint>);
            var appendLock = default(NativeList<int>);
            var allocationCount = 0;
            var currentResource = NativeDiagnosticResourceKindV1.DiagnosticRecords;
            try
            {
                records = Allocate<NativeDiagnosticRecordV1>(capacity.RecordCapacity, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                currentResource = NativeDiagnosticResourceKindV1.DiagnosticLocations;
                locations = Allocate<NativeDiagnosticLocationV1>(capacity.RelatedLocationCapacity, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                rejection = Allocate<NativeDiagnosticRecordV1>(1, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                control = Allocate<uint>(3, allocator, failAfterSuccessfulAllocations, ref allocationCount);
                if (failAfterSuccessfulAllocations >= 0 && allocationCount == failAfterSuccessfulAllocations) throw new InvalidOperationException();
                appendLock = new NativeList<int>(1, allocator);
                appendLock.Add(0);
                allocationCount++;
                if (!NativeOwnerIdentityV1.TryNext(out var ownerId))
                {
                    throw new OverflowException();
                }

                owner = new NativeDiagnosticChannelOwnerV1
                {
                    OwnerId = ownerId,
                    Generation = 1,
                    State = NativeOwnerStateV1.Initialized,
                    Capacity = capacity,
                    _treeInstanceId = treeInstanceId.Value,
                    _workerOrdinal = workerOrdinal,
                    _records = records,
                    _relatedLocations = locations,
                    _rejection = rejection,
                    _control = control,
                    _appendLock = appendLock,
                };
                failure = default;
                return true;
            }
            catch (OverflowException)
            {
                DisposeReverse(ref appendLock, ref control, ref rejection, ref locations, ref records);
                failure = new NativeDiagnosticChannelFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeDiagnosticResourceKindV1.LeaseCounter);
                return false;
            }
            catch (Exception)
            {
                DisposeReverse(ref appendLock, ref control, ref rejection, ref locations, ref records);
                failure = new NativeDiagnosticChannelFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeDiagnosticCapacityExceeded,
                    currentResource,
                    (ulong)allocationCount + 1,
                    (ulong)allocationCount);
                return false;
            }
        }

        public bool TryAcquireWriter(out NativeDiagnosticChannelLeaseV1 lease, out NativeDiagnosticChannelFailureV1 failure)
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
                failure = new NativeDiagnosticChannelFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeDiagnosticResourceKindV1.LeaseCounter,
                    _nextLeaseId,
                    ulong.MaxValue,
                    OwnerId,
                    Generation);
                return false;
            }

            _activeLease = new NativeLeaseTokenV1(OwnerId, Generation, ++_nextLeaseId);
            _dependency = default;
            State = NativeOwnerStateV1.Executing;
            lease = new NativeDiagnosticChannelLeaseV1(
                this,
                _activeLease,
                new NativeDiagnosticWriterV1(_records, _relatedLocations, _rejection, _control, _appendLock, _treeInstanceId, _workerOrdinal));
            failure = default;
            return true;
        }

        public bool TryRegisterDependency(
            NativeDiagnosticChannelLeaseV1 lease,
            JobHandle dependency,
            out NativeDiagnosticChannelFailureV1 failure)
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

        public bool TryReleaseWriter(NativeDiagnosticChannelLeaseV1 lease, out NativeDiagnosticChannelFailureV1 failure)
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

        public bool TryGetSnapshot(out NativeDiagnosticChannelSnapshotV1 snapshot, out NativeDiagnosticChannelFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized)
            {
                snapshot = default;
                failure = State == NativeOwnerStateV1.Executing ? LiveFailure(_activeLease.LeaseId) : LifetimeFailure(0);
                return false;
            }

            snapshot = new NativeDiagnosticChannelSnapshotV1(
                _records.AsReadOnly(),
                _relatedLocations.AsReadOnly(),
                _control[0],
                _control[1],
                _control[2] != 0,
                _rejection[0]);
            failure = default;
            return true;
        }

        public bool TryReset(out NativeDiagnosticChannelFailureV1 failure)
        {
            if (State != NativeOwnerStateV1.Initialized)
            {
                failure = State == NativeOwnerStateV1.Executing ? LiveFailure(_activeLease.LeaseId) : LifetimeFailure(0);
                return false;
            }

            if (Generation == uint.MaxValue)
            {
                failure = new NativeDiagnosticChannelFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                    NativeDiagnosticResourceKindV1.LeaseCounter,
                    Generation,
                    uint.MaxValue,
                    OwnerId,
                    Generation);
                return false;
            }

            Generation++;
            Clear(_records);
            Clear(_relatedLocations);
            Clear(_rejection);
            Clear(_control);
            failure = default;
            return true;
        }

        public bool TryDispose(out NativeDiagnosticChannelFailureV1 failure)
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

            DisposeReverse(ref _appendLock, ref _control, ref _rejection, ref _relatedLocations, ref _records);
            State = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        private bool IsActive(NativeDiagnosticChannelLeaseV1 lease)
            => ReferenceEquals(lease.Owner, this) && lease.Token == _activeLease && State == NativeOwnerStateV1.Executing;

        private NativeDiagnosticChannelFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeDiagnosticChannelFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private NativeDiagnosticChannelFailureV1 LiveFailure(ulong leaseId)
            => new NativeDiagnosticChannelFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private static NativeArray<T> Allocate<T>(uint count, Allocator allocator, int failAfter, ref int allocationCount)
            where T : struct
        {
            if (failAfter >= 0 && allocationCount == failAfter)
            {
                throw new InvalidOperationException();
            }

            var array = new NativeArray<T>((int)count, allocator, NativeArrayOptions.ClearMemory);
            allocationCount++;
            return array;
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            for (var index = 0; index < array.Length; index++) array[index] = default;
        }

        private static void DisposeReverse(
            ref NativeList<int> appendLock,
            ref NativeArray<uint> control,
            ref NativeArray<NativeDiagnosticRecordV1> rejection,
            ref NativeArray<NativeDiagnosticLocationV1> locations,
            ref NativeArray<NativeDiagnosticRecordV1> records)
        {
            if (appendLock.IsCreated)
            {
                appendLock.Dispose();
                appendLock = default;
            }
            Dispose(ref control);
            Dispose(ref rejection);
            Dispose(ref locations);
            Dispose(ref records);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }
    }

    public readonly struct NativeDiagnosticChannelLeaseV1
    {
        internal NativeDiagnosticChannelLeaseV1(
            NativeDiagnosticChannelOwnerV1 owner,
            NativeLeaseTokenV1 token,
            NativeDiagnosticWriterV1 writer)
        {
            Owner = owner;
            Token = token;
            Writer = writer;
        }

        internal NativeDiagnosticChannelOwnerV1 Owner { get; }
        public NativeLeaseTokenV1 Token { get; }
        public NativeDiagnosticWriterV1 Writer { get; }
    }

    internal static class NativeDiagnosticContractV1
    {
        internal static bool ValidateFields(in NativeDiagnosticRecordV1 record)
        {
            NativeDiagnosticFieldIdV1 previous = NativeDiagnosticFieldIdV1.None;
            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);
                if (!field.IsValid || field.FieldId <= previous)
                {
                    return false;
                }

                previous = field.FieldId;
            }

            switch (record.CodeNumber)
            {
                case 4301: return HasExact(record, 2, Bit(1) | Bit(2));
                case 4302: return HasExact(record, 4, Bit(3) | Bit(4) | Bit(5) | Bit(6));
                case 4303: return HasExact(record, 4, Bit(3) | Bit(7) | Bit(8) | Bit(9));
                case 4304:
                case 4305:
                case 4306:
                case 4307:
                case 4308:
                case 4309:
                    return HasExact(record, 3, Bit(3) | Bit(4) | Bit(5));
                case 4310: return HasExact(record, 4, Bit(3) | Bit(4) | Bit(5) | Bit(14));
                case 4311:
                case 4312:
                    return HasExact(record, 6, Bit(1) | Bit(7) | Bit(10) | Bit(11) | Bit(12) | Bit(13));
                default:
                    return true;
            }
        }

        private static bool HasExact(in NativeDiagnosticRecordV1 record, byte count, uint expectedMask)
        {
            if (record.FieldCount != count)
            {
                return false;
            }

            uint actualMask = 0;
            for (var index = 0; index < record.FieldCount; index++)
            {
                actualMask |= Bit((byte)record.GetField(index).FieldId);
            }

            return actualMask == expectedMask;
        }

        private static uint Bit(byte fieldId) => 1u << fieldId;
    }

    internal static class NativeDiagnosticOrderV1
    {
        internal static int CompareRejection(in NativeDiagnosticRecordV1 left, in NativeDiagnosticRecordV1 right)
        {
            var result = ((byte)left.Phase).CompareTo((byte)right.Phase);
            if (result != 0) return result;
            result = left.PrimaryLocation.TreeInstanceId.CompareTo(right.PrimaryLocation.TreeInstanceId);
            if (result != 0) return result;
            result = ((byte)left.PrimaryLocation.Flags).CompareTo((byte)right.PrimaryLocation.Flags);
            if (result != 0) return result;
            result = left.PrimaryLocation.RuntimeNodeIndex.CompareTo(right.PrimaryLocation.RuntimeNodeIndex);
            if (result != 0) return result;
            result = left.PrimaryLocation.DebugIdentityIndex.CompareTo(right.PrimaryLocation.DebugIdentityIndex);
            if (result != 0) return result;
            result = left.WorkerOrdinal.CompareTo(right.WorkerOrdinal);
            if (result != 0) return result;
            result = left.CodeNumber.CompareTo(right.CodeNumber);
            if (result != 0) return result;
            result = left.UpdateId.CompareTo(right.UpdateId);
            if (result != 0) return result;
            result = left.SnapshotRevision.CompareTo(right.SnapshotRevision);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }

        internal static int Compare(in NativeDiagnosticRecordV1 left, in NativeDiagnosticRecordV1 right)
        {
            var result = ((byte)left.Severity).CompareTo((byte)right.Severity);
            if (result != 0) return result;
            result = left.CodeNumber.CompareTo(right.CodeNumber);
            if (result != 0) return result;
            result = CompareLocation(left.PrimaryLocation, right.PrimaryLocation);
            if (result != 0) return result;
            var shared = Math.Min(left.FieldCount, right.FieldCount);
            for (var index = 0; index < shared; index++)
            {
                var leftField = left.GetField(index);
                var rightField = right.GetField(index);
                result = ((byte)leftField.FieldId).CompareTo((byte)rightField.FieldId);
                if (result != 0) return result;
                result = ((byte)leftField.ValueKind).CompareTo((byte)rightField.ValueKind);
                if (result != 0) return result;
                result = leftField.Value.CompareTo(rightField.Value);
                if (result != 0) return result;
            }

            result = left.FieldCount.CompareTo(right.FieldCount);
            if (result != 0) return result;
            result = ((byte)left.Phase).CompareTo((byte)right.Phase);
            if (result != 0) return result;
            result = left.Sequence.CompareTo(right.Sequence);
            return result != 0 ? result : left.WorkerOrdinal.CompareTo(right.WorkerOrdinal);
        }

        internal static bool SameNormalized(in NativeDiagnosticRecordV1 left, in NativeDiagnosticRecordV1 right)
        {
            if (left.CodeNumber != right.CodeNumber
                || left.Severity != right.Severity
                || left.PrimaryLocation != right.PrimaryLocation
                || left.FieldCount != right.FieldCount
                || left.RelatedLocationCount != 0
                || right.RelatedLocationCount != 0)
            {
                return false;
            }

            for (var index = 0; index < left.FieldCount; index++)
            {
                if (!left.GetField(index).Equals(right.GetField(index))) return false;
            }

            return true;
        }

        private static int CompareLocation(in NativeDiagnosticLocationV1 left, in NativeDiagnosticLocationV1 right)
        {
            var result = Has(left, NativeDiagnosticLocationFlagsV1.TreeInstance).CompareTo(Has(right, NativeDiagnosticLocationFlagsV1.TreeInstance));
            if (result != 0) return result;
            result = left.TreeInstanceId.CompareTo(right.TreeInstanceId);
            if (result != 0) return result;
            result = Has(left, NativeDiagnosticLocationFlagsV1.DebugIdentity).CompareTo(Has(right, NativeDiagnosticLocationFlagsV1.DebugIdentity));
            if (result != 0) return result;
            result = left.DebugIdentityIndex.CompareTo(right.DebugIdentityIndex);
            if (result != 0) return result;
            result = Has(left, NativeDiagnosticLocationFlagsV1.RuntimeNode).CompareTo(Has(right, NativeDiagnosticLocationFlagsV1.RuntimeNode));
            return result != 0 ? result : left.RuntimeNodeIndex.CompareTo(right.RuntimeNodeIndex);
        }

        private static byte Has(in NativeDiagnosticLocationV1 location, NativeDiagnosticLocationFlagsV1 flag)
            => (byte)(((location.Flags & flag) != 0) ? 1 : 0);
    }

    public static class NativeDiagnosticMergeV1
    {
        public static bool TryMerge(
            in NativeDiagnosticChannelSnapshotV1 first,
            in NativeDiagnosticChannelSnapshotV1 second,
            NativeArray<NativeDiagnosticRecordV1> destinationRecords,
            NativeArray<NativeDiagnosticLocationV1> destinationLocations,
            out uint mergedRecordCount,
            out uint mergedLocationCount)
        {
            mergedRecordCount = 0;
            mergedLocationCount = 0;
            if (first.IsFaulted || second.IsFaulted
                || !ValidateSource(first.Records, first.RecordCount, first.RelatedLocations, first.RelatedLocationCount)
                || !ValidateSource(second.Records, second.RecordCount, second.RelatedLocations, second.RelatedLocationCount))
            {
                return false;
            }

            ulong requiredRecords = 0;
            ulong requiredLocations = 0;
            CountRepresentatives(first, true, second, ref requiredRecords, ref requiredLocations);
            CountRepresentatives(second, false, first, ref requiredRecords, ref requiredLocations);
            if (requiredRecords > (uint)destinationRecords.Length
                || requiredLocations > (uint)destinationLocations.Length)
            {
                return false;
            }

            InsertRepresentatives(first, true, second, destinationRecords, destinationLocations, ref mergedRecordCount, ref mergedLocationCount);
            InsertRepresentatives(second, false, first, destinationRecords, destinationLocations, ref mergedRecordCount, ref mergedLocationCount);
            return true;
        }

        public static bool TrySelectRejection(
            NativeArray<NativeDiagnosticRecordV1>.ReadOnly source,
            uint sourceCount,
            out NativeDiagnosticRecordV1 rejection)
        {
            rejection = default;
            if (sourceCount == 0 || sourceCount > source.Length)
            {
                return false;
            }

            for (var index = 0u; index < sourceCount; index++)
            {
                var candidate = source[(int)index];
                if (!candidate.HasValidHeader || !NativeDiagnosticContractV1.ValidateFields(candidate))
                {
                    rejection = default;
                    return false;
                }

                if (index == 0 || NativeDiagnosticOrderV1.CompareRejection(candidate, rejection) < 0)
                {
                    rejection = candidate;
                }
            }

            return true;
        }

        public static bool TryMergeRecords(
            NativeArray<NativeDiagnosticRecordV1>.ReadOnly source,
            uint sourceCount,
            NativeArray<NativeDiagnosticRecordV1> destination,
            out uint mergedCount)
        {
            mergedCount = 0;
            if (sourceCount > source.Length || sourceCount > destination.Length)
            {
                return false;
            }

            for (var index = 0u; index < sourceCount; index++)
            {
                var record = source[(int)index];
                if (!record.HasValidHeader || record.RelatedLocationCount != 0 || !NativeDiagnosticContractV1.ValidateFields(record))
                {
                    return false;
                }

                var insert = mergedCount;
                while (insert > 0 && NativeDiagnosticOrderV1.Compare(record, destination[(int)insert - 1]) < 0)
                {
                    destination[(int)insert] = destination[(int)insert - 1];
                    insert--;
                }

                destination[(int)insert] = record;
                mergedCount++;
            }

            if (mergedCount == 0) return true;
            var write = 1u;
            for (var read = 1u; read < mergedCount; read++)
            {
                if (NativeDiagnosticOrderV1.SameNormalized(destination[(int)write - 1], destination[(int)read]))
                {
                    continue;
                }

                destination[(int)write++] = destination[(int)read];
            }

            mergedCount = write;
            return true;
        }

        public static bool TryMerge(
            NativeArray<NativeDiagnosticRecordV1>.ReadOnly sourceRecords,
            uint sourceRecordCount,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly sourceLocations,
            uint sourceLocationCount,
            NativeArray<NativeDiagnosticRecordV1> destinationRecords,
            NativeArray<NativeDiagnosticLocationV1> destinationLocations,
            out uint mergedRecordCount,
            out uint mergedLocationCount)
        {
            mergedRecordCount = 0;
            mergedLocationCount = 0;
            if (sourceRecordCount > sourceRecords.Length || sourceLocationCount > sourceLocations.Length)
            {
                return false;
            }

            ulong requiredRecords = 0;
            ulong requiredLocations = 0;
            for (var index = 0u; index < sourceRecordCount; index++)
            {
                var record = sourceRecords[(int)index];
                if (!record.HasValidHeader
                    || !NativeDiagnosticContractV1.ValidateFields(record)
                    || (ulong)record.RelatedLocationOffset + record.RelatedLocationCount > sourceLocationCount)
                {
                    return false;
                }

                for (var locationIndex = 0u; locationIndex < record.RelatedLocationCount; locationIndex++)
                {
                    if (!sourceLocations[(int)(record.RelatedLocationOffset + locationIndex)].IsValid) return false;
                }

                if (!IsCanonicalRepresentative(sourceRecords, sourceRecordCount, sourceLocations, index)) continue;
                requiredRecords++;
                requiredLocations += record.RelatedLocationCount;
                if (requiredRecords > (uint)destinationRecords.Length
                    || requiredLocations > (uint)destinationLocations.Length)
                {
                    return false;
                }
            }

            for (var sourceIndex = 0u; sourceIndex < sourceRecordCount; sourceIndex++)
            {
                if (!IsCanonicalRepresentative(sourceRecords, sourceRecordCount, sourceLocations, sourceIndex)) continue;
                var record = sourceRecords[(int)sourceIndex];
                var insert = mergedRecordCount;
                while (insert > 0 && CompareForMerge(
                    record, sourceLocations, record.RelatedLocationOffset,
                    destinationRecords[(int)insert - 1], destinationLocations,
                    destinationRecords[(int)insert - 1].RelatedLocationOffset) < 0)
                {
                    insert--;
                }

                var locationOffset = insert < mergedRecordCount
                    ? destinationRecords[(int)insert].RelatedLocationOffset
                    : mergedLocationCount;
                for (var locationIndex = mergedLocationCount; locationIndex > locationOffset; locationIndex--)
                {
                    destinationLocations[(int)(locationIndex - 1 + record.RelatedLocationCount)]
                        = destinationLocations[(int)locationIndex - 1];
                }

                for (var recordIndex = mergedRecordCount; recordIndex > insert; recordIndex--)
                {
                    var moved = destinationRecords[(int)recordIndex - 1];
                    moved.RelatedLocationOffset += record.RelatedLocationCount;
                    destinationRecords[(int)recordIndex] = moved;
                }

                for (var locationIndex = 0u; locationIndex < record.RelatedLocationCount; locationIndex++)
                {
                    destinationLocations[(int)(locationOffset + locationIndex)]
                        = sourceLocations[(int)(record.RelatedLocationOffset + locationIndex)];
                }

                record.RelatedLocationOffset = locationOffset;
                destinationRecords[(int)insert] = record;
                mergedRecordCount++;
                mergedLocationCount += record.RelatedLocationCount;
            }

            return true;
        }

        private static bool IsCanonicalRepresentative(
            NativeArray<NativeDiagnosticRecordV1>.ReadOnly records,
            uint recordCount,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly locations,
            uint index)
        {
            var record = records[(int)index];
            for (var candidateIndex = 0u; candidateIndex < recordCount; candidateIndex++)
            {
                if (candidateIndex == index) continue;
                var candidate = records[(int)candidateIndex];
                if (!SameNormalizedWithLocations(record, locations, candidate, locations)) continue;
                var order = NativeDiagnosticOrderV1.Compare(candidate, record);
                if (order < 0 || order == 0 && candidateIndex < index) return false;
            }

            return true;
        }

        private static bool SameNormalizedWithLocations(
            in NativeDiagnosticRecordV1 left,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly leftLocations,
            in NativeDiagnosticRecordV1 right,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly rightLocations)
        {
            if (left.CodeNumber != right.CodeNumber
                || left.Severity != right.Severity
                || left.PrimaryLocation != right.PrimaryLocation
                || left.FieldCount != right.FieldCount
                || left.RelatedLocationCount != right.RelatedLocationCount)
            {
                return false;
            }

            for (var index = 0; index < left.FieldCount; index++)
            {
                if (!left.GetField(index).Equals(right.GetField(index))) return false;
            }

            for (var index = 0u; index < left.RelatedLocationCount; index++)
            {
                if (leftLocations[(int)(left.RelatedLocationOffset + index)]
                    != rightLocations[(int)(right.RelatedLocationOffset + index)]) return false;
            }

            return true;
        }

        private static int CompareForMerge(
            in NativeDiagnosticRecordV1 left,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly leftLocations,
            uint leftOffset,
            in NativeDiagnosticRecordV1 right,
            NativeArray<NativeDiagnosticLocationV1> rightLocations,
            uint rightOffset)
        {
            var result = NativeDiagnosticOrderV1.Compare(left, right);
            if (result != 0) return result;
            var shared = Math.Min(left.RelatedLocationCount, right.RelatedLocationCount);
            for (var index = 0u; index < shared; index++)
            {
                result = CompareLocation(
                    leftLocations[(int)(leftOffset + index)],
                    rightLocations[(int)(rightOffset + index)]);
                if (result != 0) return result;
            }

            return left.RelatedLocationCount.CompareTo(right.RelatedLocationCount);
        }

        private static int CompareLocation(in NativeDiagnosticLocationV1 left, in NativeDiagnosticLocationV1 right)
        {
            var result = ((byte)left.Flags).CompareTo((byte)right.Flags);
            if (result != 0) return result;
            result = left.TreeInstanceId.CompareTo(right.TreeInstanceId);
            if (result != 0) return result;
            result = left.RuntimeNodeIndex.CompareTo(right.RuntimeNodeIndex);
            return result != 0 ? result : left.DebugIdentityIndex.CompareTo(right.DebugIdentityIndex);
        }

        private static bool ValidateSource(
            NativeArray<NativeDiagnosticRecordV1>.ReadOnly records,
            uint recordCount,
            NativeArray<NativeDiagnosticLocationV1>.ReadOnly locations,
            uint locationCount)
        {
            if (recordCount > records.Length || locationCount > locations.Length) return false;
            for (var index = 0u; index < recordCount; index++)
            {
                var record = records[(int)index];
                if (!record.HasValidHeader
                    || !NativeDiagnosticContractV1.ValidateFields(record)
                    || (ulong)record.RelatedLocationOffset + record.RelatedLocationCount > locationCount)
                {
                    return false;
                }

                for (var locationIndex = 0u; locationIndex < record.RelatedLocationCount; locationIndex++)
                {
                    if (!locations[(int)(record.RelatedLocationOffset + locationIndex)].IsValid) return false;
                }
            }

            return true;
        }

        private static void CountRepresentatives(
            in NativeDiagnosticChannelSnapshotV1 source,
            bool sourceIsFirst,
            in NativeDiagnosticChannelSnapshotV1 other,
            ref ulong recordCount,
            ref ulong locationCount)
        {
            for (var index = 0u; index < source.RecordCount; index++)
            {
                if (!IsRepresentative(source, sourceIsFirst, index, other)) continue;
                recordCount++;
                locationCount += source.Records[(int)index].RelatedLocationCount;
            }
        }

        private static bool IsRepresentative(
            in NativeDiagnosticChannelSnapshotV1 source,
            bool sourceIsFirst,
            uint index,
            in NativeDiagnosticChannelSnapshotV1 other)
        {
            var record = source.Records[(int)index];
            for (var candidateIndex = 0u; candidateIndex < source.RecordCount; candidateIndex++)
            {
                if (candidateIndex == index) continue;
                var candidate = source.Records[(int)candidateIndex];
                if (!SameNormalizedWithLocations(record, source.RelatedLocations, candidate, source.RelatedLocations)) continue;
                var order = NativeDiagnosticOrderV1.Compare(candidate, record);
                if (order < 0 || order == 0 && candidateIndex < index) return false;
            }

            for (var candidateIndex = 0u; candidateIndex < other.RecordCount; candidateIndex++)
            {
                var candidate = other.Records[(int)candidateIndex];
                if (!SameNormalizedWithLocations(record, source.RelatedLocations, candidate, other.RelatedLocations)) continue;
                var order = NativeDiagnosticOrderV1.Compare(candidate, record);
                if (order < 0 || order == 0 && !sourceIsFirst) return false;
            }

            return true;
        }

        private static void InsertRepresentatives(
            in NativeDiagnosticChannelSnapshotV1 source,
            bool sourceIsFirst,
            in NativeDiagnosticChannelSnapshotV1 other,
            NativeArray<NativeDiagnosticRecordV1> destinationRecords,
            NativeArray<NativeDiagnosticLocationV1> destinationLocations,
            ref uint mergedRecordCount,
            ref uint mergedLocationCount)
        {
            for (var sourceIndex = 0u; sourceIndex < source.RecordCount; sourceIndex++)
            {
                if (!IsRepresentative(source, sourceIsFirst, sourceIndex, other)) continue;
                var record = source.Records[(int)sourceIndex];
                var insert = mergedRecordCount;
                while (insert > 0 && CompareForMerge(
                    record, source.RelatedLocations, record.RelatedLocationOffset,
                    destinationRecords[(int)insert - 1], destinationLocations,
                    destinationRecords[(int)insert - 1].RelatedLocationOffset) < 0)
                {
                    insert--;
                }

                var locationOffset = insert < mergedRecordCount
                    ? destinationRecords[(int)insert].RelatedLocationOffset
                    : mergedLocationCount;
                for (var locationIndex = mergedLocationCount; locationIndex > locationOffset; locationIndex--)
                {
                    destinationLocations[(int)(locationIndex - 1 + record.RelatedLocationCount)]
                        = destinationLocations[(int)locationIndex - 1];
                }

                for (var recordIndex = mergedRecordCount; recordIndex > insert; recordIndex--)
                {
                    var moved = destinationRecords[(int)recordIndex - 1];
                    moved.RelatedLocationOffset += record.RelatedLocationCount;
                    destinationRecords[(int)recordIndex] = moved;
                }

                for (var locationIndex = 0u; locationIndex < record.RelatedLocationCount; locationIndex++)
                {
                    destinationLocations[(int)(locationOffset + locationIndex)]
                        = source.RelatedLocations[(int)(record.RelatedLocationOffset + locationIndex)];
                }

                record.RelatedLocationOffset = locationOffset;
                destinationRecords[(int)insert] = record;
                mergedRecordCount++;
                mergedLocationCount += record.RelatedLocationCount;
            }
        }
    }
}
