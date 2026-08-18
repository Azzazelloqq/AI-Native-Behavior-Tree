using System;
using System.Threading;
using AIBT.Burst;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT
{
    public sealed class NativeSnapshotOwnerV1
    {
        private NativeArray<NativeSnapshotRegistryEntryV1> _entries;
        private NativeArray<byte> _payload;
        private NativeArray<NativeSnapshotLeaseStateV1>[] _leaseStates;
        private JobHandle[] _dependencies;
        private bool[] _dependencyRegistered;
        private ulong _nextLeaseId;
        private byte _state;

        private NativeSnapshotOwnerV1(
            ulong ownerId,
            ulong revision,
            NativeArray<NativeSnapshotRegistryEntryV1> entries,
            NativeArray<byte> payload,
            NativeArray<NativeSnapshotLeaseStateV1>[] leaseStates,
            JobHandle[] dependencies,
            bool[] dependencyRegistered)
        {
            OwnerId = ownerId;
            Generation = 1;
            Revision = revision;
            _entries = entries;
            _payload = payload;
            _leaseStates = leaseStates;
            _dependencies = dependencies;
            _dependencyRegistered = dependencyRegistered;
            _state = 1;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong Revision { get; }
        public uint ReaderCapacity => _leaseStates == null ? 0 : (uint)_leaseStates.Length;

        internal static BurstContextResult TryCreate(
            NativeArray<NativeSnapshotRegistryEntryV1> sourceEntries,
            uint entryCount,
            NativeArray<byte> sourcePayload,
            uint payloadCount,
            ulong revision,
            uint readerCapacity,
            out NativeSnapshotOwnerV1 owner)
        {
            owner = null;
            if (revision == 0 || readerCapacity == 0)
            {
                return BurstContextResult.PhaseViolation;
            }

            if (entryCount > int.MaxValue || payloadCount > int.MaxValue || readerCapacity > int.MaxValue)
            {
                return BurstContextResult.Overflow;
            }

            if (!NativeSnapshotOwnerIdentityV1.TryNext(out var ownerId))
            {
                return BurstContextResult.Overflow;
            }

            var entries = default(NativeArray<NativeSnapshotRegistryEntryV1>);
            var payload = default(NativeArray<byte>);
            NativeArray<NativeSnapshotLeaseStateV1>[] leaseStates = null;
            try
            {
                entries = new NativeArray<NativeSnapshotRegistryEntryV1>(
                    (int)entryCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                payload = new NativeArray<byte>(
                    (int)payloadCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);

                for (var index = 0; index < entries.Length; index++)
                {
                    var entry = sourceEntries[index];
                    entry.AccessToken = ComputeAccessToken(ownerId, 1, (uint)index, entry.Descriptor.BindingId);
                    entries[index] = entry;
                }

                for (var index = 0; index < payload.Length; index++)
                {
                    payload[index] = sourcePayload[index];
                }

                leaseStates = new NativeArray<NativeSnapshotLeaseStateV1>[readerCapacity];
                for (var index = 0; index < leaseStates.Length; index++)
                {
                    leaseStates[index] = new NativeArray<NativeSnapshotLeaseStateV1>(
                        1,
                        Allocator.Persistent,
                        NativeArrayOptions.ClearMemory);
                }

                owner = new NativeSnapshotOwnerV1(
                    ownerId,
                    revision,
                    entries,
                    payload,
                    leaseStates,
                    new JobHandle[readerCapacity],
                    new bool[readerCapacity]);
                return BurstContextResult.Success;
            }
            catch (OverflowException)
            {
                DisposeLeaseStates(leaseStates);
                Dispose(ref payload);
                Dispose(ref entries);
                return BurstContextResult.Overflow;
            }
            catch
            {
                DisposeLeaseStates(leaseStates);
                Dispose(ref payload);
                Dispose(ref entries);
                throw;
            }
        }

        public BurstContextResult TryResolve<T>(
            in NativeSnapshotTypeDescriptorV1 expected,
            out NativeSnapshotReadHandleV1<T> handle)
            where T : unmanaged
        {
            handle = default;
            if (_state != 1)
            {
                return BurstContextResult.InvalidHandle;
            }

            for (var index = 0; index < _entries.Length; index++)
            {
                var entry = _entries[index];
                if (entry.Descriptor.BindingId != expected.BindingId)
                {
                    continue;
                }

                if (entry.Descriptor != expected
                    || entry.SameProcessTypeToken != BurstRuntime.GetHashCode64<T>()
                    || !NativeSnapshotDescriptorV1.Matches<T>(expected))
                {
                    return BurstContextResult.TypeMismatch;
                }

                handle = new NativeSnapshotReadHandleV1<T>(OwnerId, Generation, (uint)index, entry.AccessToken);
                return BurstContextResult.Success;
            }

            return BurstContextResult.InvalidHandle;
        }

        public BurstContextResult TryAcquireRead(out NativeSnapshotReadLeaseV1 lease)
        {
            lease = default;
            if (_state != 1)
            {
                return BurstContextResult.InvalidHandle;
            }

            var slot = -1;
            for (var index = 0; index < _leaseStates.Length; index++)
            {
                if (_leaseStates[index][0].LeaseId == 0)
                {
                    slot = index;
                    break;
                }
            }

            if (slot < 0)
            {
                return BurstContextResult.CapacityExceeded;
            }

            if (_nextLeaseId == ulong.MaxValue)
            {
                return BurstContextResult.Overflow;
            }

            var leaseId = ++_nextLeaseId;
            var token = new NativeSnapshotLeaseTokenV1(OwnerId, Generation, leaseId);
            _leaseStates[slot][0] = new NativeSnapshotLeaseStateV1
            {
                OwnerId = OwnerId,
                Generation = Generation,
                LeaseId = leaseId,
            };
            _dependencies[slot] = default;
            _dependencyRegistered[slot] = false;
            lease = new NativeSnapshotReadLeaseV1(
                this,
                (uint)slot,
                token,
                new NativeSnapshotViewV1(
                    _entries,
                    _payload,
                    _leaseStates[slot],
                    OwnerId,
                    Generation,
                    leaseId,
                    Revision));
            return BurstContextResult.Success;
        }

        public BurstContextResult TryRegisterDependency(in NativeSnapshotReadLeaseV1 lease, JobHandle dependency)
        {
            var validation = ValidateLiveLease(lease, out var slot);
            if (validation != BurstContextResult.Success)
            {
                return validation;
            }

            if (_dependencyRegistered[slot])
            {
                return BurstContextResult.PhaseViolation;
            }

            _dependencies[slot] = dependency;
            _dependencyRegistered[slot] = true;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryRelease(in NativeSnapshotReadLeaseV1 lease)
        {
            var validation = ValidateLiveLease(lease, out var slot);
            if (validation != BurstContextResult.Success)
            {
                return validation;
            }

            if (!_dependencyRegistered[slot] || !_dependencies[slot].IsCompleted)
            {
                return BurstContextResult.PhaseViolation;
            }

            _dependencies[slot].Complete();
            _dependencies[slot] = default;
            _dependencyRegistered[slot] = false;
            _leaseStates[slot][0] = default;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryDispose()
        {
            if (_state != 1)
            {
                return BurstContextResult.InvalidHandle;
            }

            for (var index = 0; index < _leaseStates.Length; index++)
            {
                if (_leaseStates[index][0].LeaseId != 0)
                {
                    return BurstContextResult.PhaseViolation;
                }
            }

            DisposeLeaseStates(_leaseStates);
            Dispose(ref _payload);
            Dispose(ref _entries);
            _leaseStates = null;
            _dependencies = null;
            _dependencyRegistered = null;
            _state = 2;
            return BurstContextResult.Success;
        }

        private BurstContextResult ValidateLiveLease(in NativeSnapshotReadLeaseV1 lease, out int slot)
        {
            slot = -1;
            if (_state != 1
                || !ReferenceEquals(lease.Owner, this)
                || !lease.Token.IsValid
                || lease.Token.OwnerId != OwnerId
                || lease.Token.Generation != Generation
                || lease.Slot >= (uint)_leaseStates.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            slot = (int)lease.Slot;
            var state = _leaseStates[slot][0];
            return state.OwnerId == OwnerId
                && state.Generation == Generation
                && state.LeaseId == lease.Token.LeaseId
                ? BurstContextResult.Success
                : BurstContextResult.InvalidHandle;
        }

        private static uint ComputeAccessToken(ulong ownerId, uint generation, uint ordinal, ulong bindingId)
        {
            unchecked
            {
                var value = ownerId ^ bindingId ^ ((ulong)generation << 32) ^ ordinal;
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdul;
                value ^= value >> 33;
                var token = (uint)(value ^ (value >> 32));
                return token == 0 ? 1u : token;
            }
        }

        private static void DisposeLeaseStates(NativeArray<NativeSnapshotLeaseStateV1>[] leaseStates)
        {
            if (leaseStates == null)
            {
                return;
            }

            for (var index = leaseStates.Length - 1; index >= 0; index--)
            {
                var state = leaseStates[index];
                Dispose(ref state);
                leaseStates[index] = default;
            }
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

    internal static class NativeSnapshotOwnerIdentityV1
    {
        private static long s_nextOwnerId;

        internal static bool TryNext(out ulong ownerId)
        {
            ownerId = 0;
            while (true)
            {
                var current = Volatile.Read(ref s_nextOwnerId);
                if (current == long.MaxValue)
                {
                    return false;
                }

                var next = current + 1;
                if (Interlocked.CompareExchange(ref s_nextOwnerId, next, current) != current)
                {
                    continue;
                }

                ownerId = (ulong)next;
                return true;
            }
        }
    }
}
