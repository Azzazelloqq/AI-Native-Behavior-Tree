using System;
using AIBT.Burst;
using Unity.Burst;
using Unity.Collections;

namespace AIBT
{
    public sealed class NativeSnapshotBuilderV1
    {
        private NativeArray<NativeSnapshotRegistryEntryV1> _entries;
        private NativeArray<byte> _payload;
        private uint _entryCount;
        private uint _payloadCount;
        private byte _state;

        private NativeSnapshotBuilderV1(
            NativeArray<NativeSnapshotRegistryEntryV1> entries,
            NativeArray<byte> payload)
        {
            _entries = entries;
            _payload = payload;
            _state = 1;
        }

        public uint EntryCount => _entryCount;
        public uint PayloadBytesUsed => _payloadCount;

        public static BurstContextResult TryCreate(
            uint bindingCapacity,
            uint payloadCapacity,
            out NativeSnapshotBuilderV1 builder)
        {
            builder = null;
            if (bindingCapacity == 0)
            {
                return BurstContextResult.CapacityExceeded;
            }

            if (bindingCapacity > int.MaxValue || payloadCapacity > int.MaxValue)
            {
                return BurstContextResult.Overflow;
            }

            var entries = default(NativeArray<NativeSnapshotRegistryEntryV1>);
            var payload = default(NativeArray<byte>);
            try
            {
                entries = new NativeArray<NativeSnapshotRegistryEntryV1>(
                    (int)bindingCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                payload = new NativeArray<byte>(
                    (int)payloadCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                builder = new NativeSnapshotBuilderV1(entries, payload);
                return BurstContextResult.Success;
            }
            catch (OverflowException)
            {
                Dispose(ref payload);
                Dispose(ref entries);
                return BurstContextResult.Overflow;
            }
            catch
            {
                Dispose(ref payload);
                Dispose(ref entries);
                throw;
            }
        }

        public BurstContextResult TryAdd<T>(in NativeSnapshotTypeDescriptorV1 descriptor, in T value)
            where T : unmanaged
        {
            var validation = ValidateDeclaration<T>(descriptor);
            if (validation != BurstContextResult.Success)
            {
                return validation;
            }

            if (_entryCount >= (uint)_entries.Length)
            {
                return BurstContextResult.CapacityExceeded;
            }

            var alignmentResult = TryAlign(_payloadCount, descriptor.Alignment, out var offset);
            if (alignmentResult != BurstContextResult.Success)
            {
                return alignmentResult;
            }

            ulong end = (ulong)offset + descriptor.Size;
            if (end > uint.MaxValue)
            {
                return BurstContextResult.Overflow;
            }

            if (end > (uint)_payload.Length)
            {
                return BurstContextResult.CapacityExceeded;
            }

            var bytes = _payload.GetSubArray((int)offset, (int)descriptor.Size);
            var typed = bytes.Reinterpret<T>(1);
            typed[0] = value;
            _entries[(int)_entryCount] = new NativeSnapshotRegistryEntryV1
            {
                Descriptor = descriptor,
                SameProcessTypeToken = BurstRuntime.GetHashCode64<T>(),
                PayloadOffset = offset,
                HasValue = 1,
            };
            _entryCount++;
            _payloadCount = (uint)end;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryDeclareMissing<T>(in NativeSnapshotTypeDescriptorV1 descriptor)
            where T : unmanaged
        {
            var validation = ValidateDeclaration<T>(descriptor);
            if (validation != BurstContextResult.Success)
            {
                return validation;
            }

            if (_entryCount >= (uint)_entries.Length)
            {
                return BurstContextResult.CapacityExceeded;
            }

            _entries[(int)_entryCount] = new NativeSnapshotRegistryEntryV1
            {
                Descriptor = descriptor,
                SameProcessTypeToken = BurstRuntime.GetHashCode64<T>(),
                PayloadOffset = 0,
                HasValue = 0,
            };
            _entryCount++;
            return BurstContextResult.Success;
        }

        public BurstContextResult TryFreeze(
            ulong revision,
            uint readerCapacity,
            out NativeSnapshotOwnerV1 owner)
        {
            owner = null;
            if (_state != 1 || revision == 0)
            {
                return BurstContextResult.PhaseViolation;
            }

            if (readerCapacity == 0)
            {
                return BurstContextResult.CapacityExceeded;
            }

            var result = NativeSnapshotOwnerV1.TryCreate(
                _entries,
                _entryCount,
                _payload,
                _payloadCount,
                revision,
                readerCapacity,
                out owner);
            if (result == BurstContextResult.Success)
            {
                _state = 2;
            }

            return result;
        }

        public BurstContextResult TryDispose()
        {
            if (_state == 0 || _state == 3)
            {
                return BurstContextResult.InvalidHandle;
            }

            Dispose(ref _payload);
            Dispose(ref _entries);
            _state = 3;
            return BurstContextResult.Success;
        }

        private BurstContextResult ValidateDeclaration<T>(in NativeSnapshotTypeDescriptorV1 descriptor)
            where T : unmanaged
        {
            if (_state != 1)
            {
                return BurstContextResult.PhaseViolation;
            }

            if (!NativeSnapshotDescriptorV1.Matches<T>(descriptor))
            {
                return BurstContextResult.TypeMismatch;
            }

            for (var index = 0; index < _entryCount; index++)
            {
                var existing = _entries[index].Descriptor;
                if (existing.BindingId != descriptor.BindingId)
                {
                    continue;
                }

                return existing == descriptor
                    ? BurstContextResult.PhaseViolation
                    : BurstContextResult.TypeMismatch;
            }

            return BurstContextResult.Success;
        }

        private static BurstContextResult TryAlign(uint value, uint alignment, out uint aligned)
        {
            aligned = 0;
            if (!NativeSnapshotDescriptorV1.IsPowerOfTwo(alignment))
            {
                return BurstContextResult.TypeMismatch;
            }

            ulong candidate = ((ulong)value + alignment - 1) & ~(ulong)(alignment - 1);
            if (candidate > uint.MaxValue)
            {
                return BurstContextResult.Overflow;
            }

            aligned = (uint)candidate;
            return BurstContextResult.Success;
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
}
