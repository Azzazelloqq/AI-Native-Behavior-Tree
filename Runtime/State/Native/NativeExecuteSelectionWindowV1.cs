using System;
using System.Threading;
using Unity.Collections;

namespace AIBT
{
    public readonly struct NativeExecuteSelectionCapacityV1
    {
        public NativeExecuteSelectionCapacityV1(uint maximumEntries, uint maximumReaders)
        { MaximumEntries = maximumEntries; MaximumReaders = maximumReaders; }

        public uint MaximumEntries { get; }
        public uint MaximumReaders { get; }
    }

    public readonly struct NativeExecuteSelectionEntryV1
    {
        public NativeExecuteSelectionEntryV1(
            TreeInstanceId treeInstanceId,
            uint sharedRecordCapacity,
            uint sharedPayloadCapacity)
        {
            TreeInstanceId = treeInstanceId;
            SharedRecordCapacity = sharedRecordCapacity;
            SharedPayloadCapacity = sharedPayloadCapacity;
        }

        public TreeInstanceId TreeInstanceId { get; }
        public uint SharedRecordCapacity { get; }
        public uint SharedPayloadCapacity { get; }
        public bool HasSharedCapacity => SharedRecordCapacity != 0 && SharedPayloadCapacity != 0;
    }

    public readonly struct NativeExecuteSelectionWindowV1
    {
        internal NativeExecuteSelectionWindowV1(
            ulong ownerId,
            uint generation,
            ulong windowId,
            uint count)
        { OwnerId = ownerId; Generation = generation; WindowId = windowId; Count = count; }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong WindowId { get; }
        public uint Count { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && WindowId != 0 && Count != 0;
    }

    public readonly struct NativeExecuteSelectionViewV1
    {
        internal NativeExecuteSelectionViewV1(
            NativeArray<NativeExecuteSelectionEntryV1>.ReadOnly entries,
            uint count)
        { Entries = entries; Count = count; }

        public NativeArray<NativeExecuteSelectionEntryV1>.ReadOnly Entries { get; }
        public uint Count { get; }
        public bool IsCreated => Entries.IsCreated && Count != 0 && Count <= Entries.Length;
    }

    public readonly struct NativeExecuteSelectionReadLeaseV1
    {
        internal NativeExecuteSelectionReadLeaseV1(
            NativeExecuteSelectionWindowOwnerV1 owner,
            ulong ownerId,
            uint generation,
            ulong leaseId,
            NativeExecuteSelectionWindowV1 window,
            NativeExecuteSelectionViewV1 view)
        {
            Owner = owner;
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
            Window = window;
            View = view;
        }

        internal NativeExecuteSelectionWindowOwnerV1 Owner { get; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public NativeExecuteSelectionWindowV1 Window { get; }
        public NativeExecuteSelectionViewV1 View { get; }
        public bool IsValid => Owner != null && OwnerId != 0 && Generation != 0
            && LeaseId != 0 && Window.IsValid && View.IsCreated;
    }

    public sealed class NativeExecuteSelectionWindowOwnerV1
    {
        private struct ReaderEntry
        {
            internal bool Active;
            internal ulong LeaseId;
            internal ulong WindowId;
        }

        private static long s_nextOwnerId;
        private NativeArray<NativeExecuteSelectionEntryV1> _entries;
        private NativeArray<ReaderEntry> _readers;
        private NativeExecuteSelectionWindowV1 _window;
        private NativeOwnerStateV1 _state;
        private uint _entryCount;
        private uint _readerCount;
        private ulong _nextWindowId;
        private ulong _nextReaderLeaseId;

        private NativeExecuteSelectionWindowOwnerV1() { }

        public ulong OwnerId { get; private set; }
        public uint Generation { get; private set; }
        public NativeOwnerStateV1 State => _state;
        public NativeExecuteSelectionCapacityV1 Capacity { get; private set; }

        public static bool TryCreate(
            NativeExecuteSelectionCapacityV1 capacity,
            Allocator allocator,
            out NativeExecuteSelectionWindowOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
            => TryCreate(capacity, allocator, -1, out owner, out failure);

        private static bool TryCreate(
            NativeExecuteSelectionCapacityV1 capacity,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            out NativeExecuteSelectionWindowOwnerV1 owner,
            out NativeRuntimeFailureV1 failure)
        {
            owner = null;
            if (capacity.MaximumEntries == 0 || capacity.MaximumReaders == 0
                || capacity.MaximumEntries > int.MaxValue || capacity.MaximumReaders > int.MaxValue)
            {
                failure = new NativeRuntimeFailureV1(
                    capacity.MaximumEntries > int.MaxValue || capacity.MaximumReaders > int.MaxValue
                        ? NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow
                        : NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    capacity.MaximumEntries == 0 || capacity.MaximumEntries > int.MaxValue
                        ? NativeResourceKindV1.ExecuteSelectionEntries
                        : NativeResourceKindV1.ExecuteSelectionReaders,
                    capacity.MaximumEntries == 0 || capacity.MaximumEntries > int.MaxValue
                        ? capacity.MaximumEntries
                        : capacity.MaximumReaders,
                    int.MaxValue);
                return false;
            }
            if (allocator != Allocator.Persistent)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeAllocatorInvalid,
                    NativeResourceKindV1.ExecuteSelectionEntries);
                return false;
            }

            var entries = default(NativeArray<NativeExecuteSelectionEntryV1>);
            var readers = default(NativeArray<ReaderEntry>);
            var allocations = 0;
            var currentResource = NativeResourceKindV1.ExecuteSelectionEntries;
            try
            {
                entries = Allocate<NativeExecuteSelectionEntryV1>(
                    capacity.MaximumEntries, allocator, failAfterSuccessfulAllocations, ref allocations);
                currentResource = NativeResourceKindV1.ExecuteSelectionReaders;
                readers = Allocate<ReaderEntry>(
                    capacity.MaximumReaders, allocator, failAfterSuccessfulAllocations, ref allocations);
                var rawOwnerId = Interlocked.Increment(ref s_nextOwnerId);
                if (rawOwnerId <= 0) throw new OverflowException();
                owner = new NativeExecuteSelectionWindowOwnerV1
                {
                    OwnerId = (ulong)rawOwnerId,
                    Generation = 1,
                    Capacity = capacity,
                    _entries = entries,
                    _readers = readers,
                    _state = NativeOwnerStateV1.Initialized,
                };
                failure = default;
                return true;
            }
            catch (Exception)
            {
                Dispose(ref readers);
                Dispose(ref entries);
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    currentResource);
                return false;
            }
        }

        public bool TryBegin(
            NativeArray<NativeExecuteSelectionEntryV1> entries,
            out NativeExecuteSelectionWindowV1 window,
            out NativeRuntimeFailureV1 failure)
        {
            window = default;
            if (_state != NativeOwnerStateV1.Initialized || _window.IsValid)
            {
                failure = _window.IsValid ? LiveFailure() : LifetimeFailure(0);
                return false;
            }
            if (!entries.IsCreated || entries.Length == 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.ExecuteSelectionEntries);
                return false;
            }
            if (entries.Length > _entries.Length)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.ExecuteSelectionEntries,
                    (uint)entries.Length,
                    (uint)_entries.Length);
                return false;
            }

            TreeInstanceId previous = default;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (!entry.TreeInstanceId.IsValid || index != 0 && entry.TreeInstanceId <= previous)
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch,
                        NativeResourceKindV1.ExecuteSelectionEntries);
                    return false;
                }
                if ((entry.SharedRecordCapacity == 0) != (entry.SharedPayloadCapacity == 0))
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.ExecuteSelectionEntries,
                        entry.SharedRecordCapacity == 0
                            ? entry.SharedPayloadCapacity
                            : entry.SharedRecordCapacity);
                    return false;
                }
                previous = entry.TreeInstanceId;
            }
            if (_nextWindowId == ulong.MaxValue)
            {
                failure = OverflowFailure(NativeResourceKindV1.ExecuteSelectionEntries);
                return false;
            }

            for (var index = 0; index < entries.Length; index++) _entries[index] = entries[index];
            _entryCount = (uint)entries.Length;
            _window = new NativeExecuteSelectionWindowV1(
                OwnerId, Generation, ++_nextWindowId, _entryCount);
            _state = NativeOwnerStateV1.Executing;
            window = _window;
            failure = default;
            return true;
        }

        public bool TryAcquireReadLease(
            NativeExecuteSelectionWindowV1 window,
            out NativeExecuteSelectionReadLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            lease = default;
            if (!IsWindow(window))
            {
                failure = LifetimeFailure(window.WindowId);
                return false;
            }
            var free = -1;
            for (var index = 0; index < _readers.Length; index++)
            {
                if (!_readers[index].Active) { free = index; break; }
            }
            if (free < 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded,
                    NativeResourceKindV1.ExecuteSelectionReaders,
                    (ulong)_readerCount + 1,
                    (uint)_readers.Length,
                    ownerId: OwnerId,
                    generation: Generation);
                return false;
            }
            if (_nextReaderLeaseId == ulong.MaxValue)
            {
                failure = OverflowFailure(NativeResourceKindV1.ExecuteSelectionReaders);
                return false;
            }
            var leaseId = ++_nextReaderLeaseId;
            _readers[free] = new ReaderEntry
            { Active = true, LeaseId = leaseId, WindowId = _window.WindowId };
            _readerCount++;
            lease = new NativeExecuteSelectionReadLeaseV1(
                this, OwnerId, Generation, leaseId, _window,
                new NativeExecuteSelectionViewV1(_entries.AsReadOnly(), _entryCount));
            failure = default;
            return true;
        }

        public bool TryReleaseReadLease(
            NativeExecuteSelectionReadLeaseV1 lease,
            out NativeRuntimeFailureV1 failure)
        {
            if (!lease.IsValid || !ReferenceEquals(lease.Owner, this)
                || lease.OwnerId != OwnerId || lease.Generation != Generation
                || !IsWindow(lease.Window))
            {
                failure = LifetimeFailure(lease.LeaseId);
                return false;
            }
            for (var index = 0; index < _readers.Length; index++)
            {
                var reader = _readers[index];
                if (!reader.Active || reader.LeaseId != lease.LeaseId
                    || reader.WindowId != lease.Window.WindowId) continue;
                _readers[index] = default;
                _readerCount--;
                failure = default;
                return true;
            }
            failure = LifetimeFailure(lease.LeaseId);
            return false;
        }

        public bool TryEnd(
            NativeExecuteSelectionWindowV1 window,
            out NativeRuntimeFailureV1 failure)
            => TryClose(window, out failure);

        public bool TryAbort(
            NativeExecuteSelectionWindowV1 window,
            out NativeRuntimeFailureV1 failure)
            => TryClose(window, out failure);

        public bool TryDispose(out NativeRuntimeFailureV1 failure)
        {
            if (_state == NativeOwnerStateV1.Disposed)
            {
                failure = LifetimeFailure(0);
                return false;
            }
            if (_window.IsValid || _readerCount != 0)
            {
                failure = LiveFailure();
                return false;
            }
            Dispose(ref _readers);
            Dispose(ref _entries);
            _state = NativeOwnerStateV1.Disposed;
            failure = default;
            return true;
        }

        internal bool IsLeaseActive(NativeExecuteSelectionReadLeaseV1 lease)
        {
            if (!lease.IsValid || !ReferenceEquals(lease.Owner, this)
                || lease.OwnerId != OwnerId || lease.Generation != Generation
                || !IsWindow(lease.Window)) return false;
            for (var index = 0; index < _readers.Length; index++)
                if (_readers[index].Active && _readers[index].LeaseId == lease.LeaseId
                    && _readers[index].WindowId == lease.Window.WindowId) return true;
            return false;
        }

        private bool TryClose(
            NativeExecuteSelectionWindowV1 window,
            out NativeRuntimeFailureV1 failure)
        {
            if (!IsWindow(window))
            {
                failure = LifetimeFailure(window.WindowId);
                return false;
            }
            if (_readerCount != 0)
            {
                failure = LiveFailure();
                return false;
            }
            for (var index = 0; index < _entryCount; index++) _entries[index] = default;
            _entryCount = 0;
            _window = default;
            _state = NativeOwnerStateV1.Initialized;
            failure = default;
            return true;
        }

        private bool IsWindow(NativeExecuteSelectionWindowV1 window)
            => _state == NativeOwnerStateV1.Executing && _window.IsValid
                && window.OwnerId == OwnerId && window.Generation == Generation
                && window.WindowId == _window.WindowId && window.Count == _entryCount;

        private NativeRuntimeFailureV1 LifetimeFailure(ulong leaseId)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid,
                NativeResourceKindV1.ExecuteSelectionReaders,
                ownerId: OwnerId,
                generation: Generation,
                leaseId: leaseId);

        private NativeRuntimeFailureV1 LiveFailure()
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation,
                NativeResourceKindV1.ExecuteSelectionReaders,
                ownerId: OwnerId,
                generation: Generation);

        private NativeRuntimeFailureV1 OverflowFailure(NativeResourceKindV1 resource)
            => new NativeRuntimeFailureV1(
                NativeRuntimeDiagnosticCodeV1.NativeCapacityArithmeticOverflow,
                resource,
                ownerId: OwnerId,
                generation: Generation);

        private static NativeArray<T> Allocate<T>(
            uint count,
            Allocator allocator,
            int failAfterSuccessfulAllocations,
            ref int allocations)
            where T : struct
        {
            if (failAfterSuccessfulAllocations >= 0
                && allocations >= failAfterSuccessfulAllocations)
                throw new InvalidOperationException("Injected native selection allocation failure.");
            var value = new NativeArray<T>((int)count, allocator, NativeArrayOptions.ClearMemory);
            allocations++;
            return value;
        }

        private static void Dispose<T>(ref NativeArray<T> value) where T : struct
        {
            if (!value.IsCreated) return;
            value.Dispose();
            value = default;
        }
    }
}
