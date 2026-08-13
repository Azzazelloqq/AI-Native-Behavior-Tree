using System;
using System.Collections.Generic;

namespace AIBT
{
    internal delegate bool RegisteredBlackboardEquality(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right);

    internal readonly struct RegisteredBlackboardBinding
    {
        internal RegisteredBlackboardBinding(
            RegisteredUnmanagedTypeDescriptor descriptor,
            RegisteredBlackboardEquality equality)
        {
            if (!descriptor.IsValid) throw new ArgumentException("A valid registered descriptor is required.", nameof(descriptor));
            Descriptor = descriptor;
            Equality = equality ?? throw new ArgumentNullException(nameof(equality));
        }

        internal RegisteredUnmanagedTypeDescriptor Descriptor { get; }
        internal RegisteredBlackboardEquality Equality { get; }
    }

    internal sealed class RegisteredBlackboardRegistry
    {
        private readonly Dictionary<TypeKey, RegisteredBlackboardBinding> _bindings;

        internal RegisteredBlackboardRegistry(
            IEnumerable<RegisteredBlackboardBinding> bindings,
            CompiledHash expectedHash = default)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            var values = new List<RegisteredBlackboardBinding>(bindings);
            values.Sort(BindingComparer.Instance);
            _bindings = new Dictionary<TypeKey, RegisteredBlackboardBinding>();
            for (var index = 0; index < values.Count; index++)
            {
                var binding = values[index];
                if (CollidesWithBuiltIn(binding.Descriptor))
                {
                    throw new ArgumentException(
                        "Registered blackboard bindings cannot reuse a built-in type ID and version.",
                        nameof(bindings));
                }

                var key = new TypeKey(binding.Descriptor.TypeId, binding.Descriptor.Version);
                if (_bindings.ContainsKey(key))
                {
                    throw new ArgumentException("Registered blackboard bindings must be unique by type ID and version.", nameof(bindings));
                }

                _bindings.Add(key, binding);
            }

            Hash = ComputeHash(values);
            HashMatchesExpected = !expectedHash.IsValid || expectedHash == Hash;
        }

        internal static RegisteredBlackboardRegistry Empty { get; } = new RegisteredBlackboardRegistry(Array.Empty<RegisteredBlackboardBinding>());
        internal CompiledHash Hash { get; }
        internal bool HashMatchesExpected { get; }

        internal bool TryGet(ulong typeId, uint version, out RegisteredBlackboardBinding binding)
            => _bindings.TryGetValue(new TypeKey(typeId, version), out binding);

        private static bool CollidesWithBuiltIn(RegisteredUnmanagedTypeDescriptor descriptor)
        {
            for (var raw = (int)BlackboardValueType.Bool; raw <= (int)BlackboardValueType.AssetId; raw++)
            {
                if (BuiltInBlackboardTypes.TryGet((BlackboardValueType)raw, out var builtIn)
                    && builtIn.TypeId == descriptor.TypeId
                    && builtIn.Version == descriptor.Version)
                {
                    return true;
                }
            }

            return false;
        }

        private static CompiledHash ComputeHash(IReadOnlyList<RegisteredBlackboardBinding> bindings)
        {
            var bytes = new List<byte>();
            WriteUInt32(bytes, 1);
            WriteUInt32(bytes, checked((uint)bindings.Count));
            for (var index = 0; index < bindings.Count; index++)
            {
                var descriptor = bindings[index].Descriptor;
                WriteUInt64(bytes, descriptor.TypeId);
                WriteUInt32(bytes, descriptor.Version);
                WriteUInt32(bytes, checked((uint)descriptor.Size));
                WriteUInt32(bytes, checked((uint)descriptor.Alignment));
                WriteUInt64(bytes, descriptor.EqualityContractId);
                WriteUInt64(bytes, descriptor.CanonicalSchemaId);
                WriteUInt32(bytes, descriptor.MigrationSourceVersion);
                WriteUInt64(bytes, descriptor.MigrationContractId);
            }

            return new CompiledHash(StableHash.Sha256Hex(bytes.ToArray()));
        }

        private static void WriteUInt32(List<byte> bytes, uint value)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 24));
        }

        private static void WriteUInt64(List<byte> bytes, ulong value)
        {
            WriteUInt32(bytes, (uint)value);
            WriteUInt32(bytes, (uint)(value >> 32));
        }

        private readonly struct TypeKey : IEquatable<TypeKey>
        {
            internal TypeKey(ulong id, uint version) { Id = id; Version = version; }
            private ulong Id { get; }
            private uint Version { get; }
            public bool Equals(TypeKey other) => Id == other.Id && Version == other.Version;
            public override bool Equals(object obj) => obj is TypeKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (Id.GetHashCode() * 397) ^ (int)Version; }
            }
        }

        private sealed class BindingComparer : IComparer<RegisteredBlackboardBinding>
        {
            internal static readonly BindingComparer Instance = new BindingComparer();
            public int Compare(RegisteredBlackboardBinding left, RegisteredBlackboardBinding right)
            {
                var result = left.Descriptor.TypeId.CompareTo(right.Descriptor.TypeId);
                return result != 0 ? result : left.Descriptor.Version.CompareTo(right.Descriptor.Version);
            }
        }
    }
}
