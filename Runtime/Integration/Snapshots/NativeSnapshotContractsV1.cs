using System;
using AIBT.Burst;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT
{
    /// <summary>
    /// Describes one explicitly registered snapshot binding. The schema and layout hashes,
    /// together with the physical size and alignment, make the same-process representation exact.
    /// </summary>
    public readonly struct NativeSnapshotTypeDescriptorV1 : IEquatable<NativeSnapshotTypeDescriptorV1>
    {
        public NativeSnapshotTypeDescriptorV1(
            ulong bindingId,
            ulong typeId,
            uint typeVersion,
            BurstHash256 schemaHash,
            BurstHash256 layoutHash,
            uint size,
            uint alignment)
        {
            BindingId = bindingId;
            TypeId = typeId;
            TypeVersion = typeVersion;
            SchemaHash = schemaHash;
            LayoutHash = layoutHash;
            Size = size;
            Alignment = alignment;
        }

        public ulong BindingId { get; }
        public ulong TypeId { get; }
        public uint TypeVersion { get; }
        public BurstHash256 SchemaHash { get; }
        public BurstHash256 LayoutHash { get; }
        public uint Size { get; }
        public uint Alignment { get; }

        public bool Equals(NativeSnapshotTypeDescriptorV1 other)
            => BindingId == other.BindingId
                && TypeId == other.TypeId
                && TypeVersion == other.TypeVersion
                && NativeSnapshotHashV1.Equals(SchemaHash, other.SchemaHash)
                && NativeSnapshotHashV1.Equals(LayoutHash, other.LayoutHash)
                && Size == other.Size
                && Alignment == other.Alignment;

        public override bool Equals(object obj) => obj is NativeSnapshotTypeDescriptorV1 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = BindingId.GetHashCode();
                hash = (hash * 397) ^ TypeId.GetHashCode();
                hash = (hash * 397) ^ (int)TypeVersion;
                hash = (hash * 397) ^ NativeSnapshotHashV1.GetHashCode(SchemaHash);
                hash = (hash * 397) ^ NativeSnapshotHashV1.GetHashCode(LayoutHash);
                hash = (hash * 397) ^ (int)Size;
                return (hash * 397) ^ (int)Alignment;
            }
        }

        public static bool operator ==(NativeSnapshotTypeDescriptorV1 left, NativeSnapshotTypeDescriptorV1 right)
            => left.Equals(right);

        public static bool operator !=(NativeSnapshotTypeDescriptorV1 left, NativeSnapshotTypeDescriptorV1 right)
            => !left.Equals(right);
    }

    public readonly struct NativeSnapshotReadHandleV1<T> where T : unmanaged
    {
        private readonly ulong _ownerId;
        private readonly uint _generation;
        private readonly uint _ordinal;
        private readonly uint _accessToken;

        internal NativeSnapshotReadHandleV1(ulong ownerId, uint generation, uint ordinal, uint accessToken)
        {
            _ownerId = ownerId;
            _generation = generation;
            _ordinal = ordinal;
            _accessToken = accessToken;
        }

        internal ulong OwnerId => _ownerId;
        internal uint Generation => _generation;
        internal uint Ordinal => _ordinal;
        internal uint AccessToken => _accessToken;
        public bool IsValid => _ownerId != 0 && _generation != 0 && _accessToken != 0;
    }

    public readonly struct NativeSnapshotLeaseTokenV1 : IEquatable<NativeSnapshotLeaseTokenV1>
    {
        internal NativeSnapshotLeaseTokenV1(ulong ownerId, uint generation, ulong leaseId)
        {
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public ulong LeaseId { get; }
        public bool IsValid => OwnerId != 0 && Generation != 0 && LeaseId != 0;

        public bool Equals(NativeSnapshotLeaseTokenV1 other)
            => OwnerId == other.OwnerId && Generation == other.Generation && LeaseId == other.LeaseId;

        public override bool Equals(object obj) => obj is NativeSnapshotLeaseTokenV1 other && Equals(other);
        public override int GetHashCode() => OwnerId.GetHashCode() ^ (int)Generation ^ LeaseId.GetHashCode();
        public static bool operator ==(NativeSnapshotLeaseTokenV1 left, NativeSnapshotLeaseTokenV1 right) => left.Equals(right);
        public static bool operator !=(NativeSnapshotLeaseTokenV1 left, NativeSnapshotLeaseTokenV1 right) => !left.Equals(right);
    }

    public readonly struct NativeSnapshotReadLeaseV1
    {
        internal NativeSnapshotReadLeaseV1(
            NativeSnapshotOwnerV1 owner,
            uint slot,
            NativeSnapshotLeaseTokenV1 token,
            NativeSnapshotViewV1 view)
        {
            Owner = owner;
            Slot = slot;
            Token = token;
            View = view;
        }

        internal NativeSnapshotOwnerV1 Owner { get; }
        internal uint Slot { get; }
        public NativeSnapshotLeaseTokenV1 Token { get; }
        public NativeSnapshotViewV1 View { get; }
        public bool IsValid => Owner != null && Token.IsValid;
    }

    internal struct NativeSnapshotRegistryEntryV1
    {
        internal NativeSnapshotTypeDescriptorV1 Descriptor;
        internal long SameProcessTypeToken;
        internal uint PayloadOffset;
        internal uint AccessToken;
        internal byte HasValue;
    }

    internal struct NativeSnapshotLeaseStateV1
    {
        internal ulong OwnerId;
        internal uint Generation;
        internal ulong LeaseId;
    }

    /// <summary>
    /// An immutable, non-owning job view of one snapshot revision. Payload bytes use the
    /// registered same-process unmanaged layout only; they are not persisted, hashed, or
    /// treated as a cross-platform canonical encoding.
    /// </summary>
    public readonly struct NativeSnapshotViewV1
    {
        [ReadOnly] private readonly NativeArray<NativeSnapshotRegistryEntryV1> _entries;
        [ReadOnly] private readonly NativeArray<byte> _payload;
        [ReadOnly] private readonly NativeArray<NativeSnapshotLeaseStateV1> _leaseState;
        private readonly ulong _ownerId;
        private readonly uint _generation;
        private readonly ulong _leaseId;
        private readonly ulong _revision;

        internal NativeSnapshotViewV1(
            NativeArray<NativeSnapshotRegistryEntryV1> entries,
            NativeArray<byte> payload,
            NativeArray<NativeSnapshotLeaseStateV1> leaseState,
            ulong ownerId,
            uint generation,
            ulong leaseId,
            ulong revision)
        {
            _entries = entries;
            _payload = payload;
            _leaseState = leaseState;
            _ownerId = ownerId;
            _generation = generation;
            _leaseId = leaseId;
            _revision = revision;
        }

        public ulong Revision => _revision;

        public BurstContextResult TryRead<T>(NativeSnapshotReadHandleV1<T> handle, out T value)
            where T : unmanaged
        {
            value = default;
            if (!IsLive()
                || !handle.IsValid
                || handle.OwnerId != _ownerId
                || handle.Generation != _generation
                || handle.Ordinal >= (uint)_entries.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            var entry = _entries[(int)handle.Ordinal];
            if (entry.AccessToken == 0 || entry.AccessToken != handle.AccessToken)
            {
                return BurstContextResult.InvalidHandle;
            }

            if (entry.SameProcessTypeToken != BurstRuntime.GetHashCode64<T>())
            {
                return BurstContextResult.TypeMismatch;
            }

            var size = UnsafeUtility.SizeOf<T>();
            var alignment = UnsafeUtility.AlignOf<T>();
            if (size <= 0
                || alignment <= 0
                || entry.Descriptor.Size != (uint)size
                || entry.Descriptor.Alignment != (uint)alignment)
            {
                return BurstContextResult.TypeMismatch;
            }

            if (entry.HasValue == 0)
            {
                return BurstContextResult.IncompleteValue;
            }

            if ((ulong)entry.PayloadOffset + entry.Descriptor.Size > (uint)_payload.Length)
            {
                return BurstContextResult.InvalidHandle;
            }

            var bytes = _payload.GetSubArray((int)entry.PayloadOffset, size);
            value = bytes.Reinterpret<T>(1)[0];
            return BurstContextResult.Success;
        }

        private bool IsLive()
        {
            if (_ownerId == 0
                || _generation == 0
                || _leaseId == 0
                || !_leaseState.IsCreated
                || _leaseState.Length != 1)
            {
                return false;
            }

            var state = _leaseState[0];
            return state.OwnerId == _ownerId
                && state.Generation == _generation
                && state.LeaseId == _leaseId;
        }
    }

    internal static class NativeSnapshotDescriptorV1
    {
        internal static bool IsValid(in NativeSnapshotTypeDescriptorV1 descriptor)
            => descriptor.BindingId != 0
                && descriptor.TypeId != 0
                && descriptor.TypeVersion != 0
                && descriptor.Size != 0
                && IsPowerOfTwo(descriptor.Alignment)
                && !NativeSnapshotHashV1.IsZero(descriptor.SchemaHash)
                && !NativeSnapshotHashV1.IsZero(descriptor.LayoutHash);

        internal static bool Matches<T>(in NativeSnapshotTypeDescriptorV1 descriptor) where T : unmanaged
            => IsValid(descriptor)
                && descriptor.Size == (uint)UnsafeUtility.SizeOf<T>()
                && descriptor.Alignment == (uint)UnsafeUtility.AlignOf<T>();

        internal static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    }

    internal static class NativeSnapshotHashV1
    {
        internal static bool Equals(BurstHash256 left, BurstHash256 right)
            => left.Word0 == right.Word0 && left.Word1 == right.Word1
                && left.Word2 == right.Word2 && left.Word3 == right.Word3
                && left.Word4 == right.Word4 && left.Word5 == right.Word5
                && left.Word6 == right.Word6 && left.Word7 == right.Word7;

        internal static bool IsZero(BurstHash256 value)
            => value.Word0 == 0 && value.Word1 == 0 && value.Word2 == 0 && value.Word3 == 0
                && value.Word4 == 0 && value.Word5 == 0 && value.Word6 == 0 && value.Word7 == 0;

        internal static int GetHashCode(BurstHash256 value)
        {
            unchecked
            {
                var hash = (int)value.Word0;
                hash = (hash * 397) ^ (int)value.Word1;
                hash = (hash * 397) ^ (int)value.Word2;
                hash = (hash * 397) ^ (int)value.Word3;
                hash = (hash * 397) ^ (int)value.Word4;
                hash = (hash * 397) ^ (int)value.Word5;
                hash = (hash * 397) ^ (int)value.Word6;
                return (hash * 397) ^ (int)value.Word7;
            }
        }
    }
}
