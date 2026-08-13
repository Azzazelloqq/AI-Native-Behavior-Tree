using System;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT
{
    public readonly struct BlackboardTypeDescriptor : IEquatable<BlackboardTypeDescriptor>
    {
        internal BlackboardTypeDescriptor(
            BlackboardValueType valueType,
            ulong typeId,
            uint version,
            int size,
            int alignment)
        {
            if (valueType == BlackboardValueType.Invalid)
            {
                throw new ArgumentOutOfRangeException(nameof(valueType));
            }

            ValidateLayout(typeId, version, size, alignment);
            ValueType = valueType;
            TypeId = typeId;
            Version = version;
            Size = size;
            Alignment = alignment;
        }

        public BlackboardValueType ValueType { get; }

        public ulong TypeId { get; }

        public uint Version { get; }

        public int Size { get; }

        public int Alignment { get; }

        public bool IsValid => ValueType != BlackboardValueType.Invalid
            && TypeId != 0
            && Version != 0
            && Size > 0
            && IsPowerOfTwo(Alignment);

        public static BlackboardTypeDescriptor FromRegistered(RegisteredUnmanagedTypeDescriptor descriptor)
        {
            if (!descriptor.IsValid)
            {
                throw new ArgumentException("A valid registered unmanaged type descriptor is required.", nameof(descriptor));
            }

            return new BlackboardTypeDescriptor(
                BlackboardValueType.Registered,
                descriptor.TypeId,
                descriptor.Version,
                descriptor.Size,
                descriptor.Alignment);
        }

        public bool Equals(BlackboardTypeDescriptor other)
        {
            return ValueType == other.ValueType
                && TypeId == other.TypeId
                && Version == other.Version
                && Size == other.Size
                && Alignment == other.Alignment;
        }

        public override bool Equals(object obj) => obj is BlackboardTypeDescriptor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)ValueType;
                hashCode = (hashCode * 397) ^ TypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Version;
                hashCode = (hashCode * 397) ^ Size;
                return (hashCode * 397) ^ Alignment;
            }
        }

        public static bool operator ==(BlackboardTypeDescriptor left, BlackboardTypeDescriptor right)
            => left.Equals(right);

        public static bool operator !=(BlackboardTypeDescriptor left, BlackboardTypeDescriptor right)
            => !left.Equals(right);

        private static void ValidateLayout(ulong typeId, uint version, int size, int alignment)
        {
            if (typeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId));
            }

            if (version == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (!IsPowerOfTwo(alignment))
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");
            }
        }

        private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    }

    public readonly struct RegisteredUnmanagedTypeDescriptor : IEquatable<RegisteredUnmanagedTypeDescriptor>
    {
        public RegisteredUnmanagedTypeDescriptor(
            ulong typeId,
            uint version,
            int size,
            int alignment,
            ulong equalityContractId,
            ulong canonicalSchemaId = 0,
            uint migrationSourceVersion = 0,
            ulong migrationContractId = 0)
        {
            if (typeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId));
            }

            if (version == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");
            }

            if (size % alignment != 0)
            {
                throw new ArgumentException("Size must be a multiple of alignment.", nameof(size));
            }

            if (equalityContractId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(equalityContractId));
            }

            var hasMigrationVersion = migrationSourceVersion != 0;
            var hasMigrationContract = migrationContractId != 0;
            if (hasMigrationVersion != hasMigrationContract || migrationSourceVersion >= version)
            {
                throw new ArgumentException(
                    "Migration metadata must name both an earlier source version and a nonzero migration contract.");
            }

            TypeId = typeId;
            Version = version;
            Size = size;
            Alignment = alignment;
            EqualityContractId = equalityContractId;
            CanonicalSchemaId = canonicalSchemaId;
            MigrationSourceVersion = migrationSourceVersion;
            MigrationContractId = migrationContractId;
        }

        public ulong TypeId { get; }

        public uint Version { get; }

        public int Size { get; }

        public int Alignment { get; }

        public ulong EqualityContractId { get; }

        public ulong CanonicalSchemaId { get; }

        public uint MigrationSourceVersion { get; }

        public ulong MigrationContractId { get; }

        public bool HasCanonicalSchema => CanonicalSchemaId != 0;

        public bool HasMigration => MigrationSourceVersion != 0;

        public bool IsValid => TypeId != 0
            && Version != 0
            && Size > 0
            && Alignment > 0
            && (Alignment & (Alignment - 1)) == 0
            && Size % Alignment == 0
            && EqualityContractId != 0
            && ((MigrationSourceVersion == 0 && MigrationContractId == 0)
                || (MigrationSourceVersion < Version && MigrationContractId != 0));

        public bool Equals(RegisteredUnmanagedTypeDescriptor other)
        {
            return TypeId == other.TypeId
                && Version == other.Version
                && Size == other.Size
                && Alignment == other.Alignment
                && EqualityContractId == other.EqualityContractId
                && CanonicalSchemaId == other.CanonicalSchemaId
                && MigrationSourceVersion == other.MigrationSourceVersion
                && MigrationContractId == other.MigrationContractId;
        }

        public override bool Equals(object obj) => obj is RegisteredUnmanagedTypeDescriptor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TypeId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Version;
                hashCode = (hashCode * 397) ^ Size;
                hashCode = (hashCode * 397) ^ Alignment;
                hashCode = (hashCode * 397) ^ EqualityContractId.GetHashCode();
                hashCode = (hashCode * 397) ^ CanonicalSchemaId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)MigrationSourceVersion;
                return (hashCode * 397) ^ MigrationContractId.GetHashCode();
            }
        }

        public static bool operator ==(
            RegisteredUnmanagedTypeDescriptor left,
            RegisteredUnmanagedTypeDescriptor right) => left.Equals(right);

        public static bool operator !=(
            RegisteredUnmanagedTypeDescriptor left,
            RegisteredUnmanagedTypeDescriptor right) => !left.Equals(right);
    }

    public static class BuiltInBlackboardTypes
    {
        public static readonly BlackboardTypeDescriptor Bool = Create<bool>(BlackboardValueType.Bool, "Bool");
        public static readonly BlackboardTypeDescriptor Int32 = Create<int>(BlackboardValueType.Int32, "Int32");
        public static readonly BlackboardTypeDescriptor Int64 = Create<long>(BlackboardValueType.Int64, "Int64");
        public static readonly BlackboardTypeDescriptor Float32 = Create<float>(BlackboardValueType.Float32, "Float32");
        public static readonly BlackboardTypeDescriptor Float64 = Create<double>(BlackboardValueType.Float64, "Float64");
        public static readonly BlackboardTypeDescriptor Float2 = Create<Float2Value>(BlackboardValueType.Float2, "Float2");
        public static readonly BlackboardTypeDescriptor Float3 = Create<Float3Value>(BlackboardValueType.Float3, "Float3");
        public static readonly BlackboardTypeDescriptor Quaternion = Create<QuaternionValue>(BlackboardValueType.Quaternion, "Quaternion");
        public static readonly BlackboardTypeDescriptor Enum32 = Create<Enum32Value>(BlackboardValueType.Enum32, "Enum32");
        public static readonly BlackboardTypeDescriptor FixedString32 = Create<Unity.Collections.FixedString32Bytes>(BlackboardValueType.FixedString32, "FixedString32");
        public static readonly BlackboardTypeDescriptor FixedString64 = Create<Unity.Collections.FixedString64Bytes>(BlackboardValueType.FixedString64, "FixedString64");
        public static readonly BlackboardTypeDescriptor FixedString128 = Create<Unity.Collections.FixedString128Bytes>(BlackboardValueType.FixedString128, "FixedString128");
        public static readonly BlackboardTypeDescriptor FixedString512 = Create<Unity.Collections.FixedString512Bytes>(BlackboardValueType.FixedString512, "FixedString512");
        public static readonly BlackboardTypeDescriptor AgentId = Create<AIBT.AgentId>(BlackboardValueType.AgentId, "AgentId");
        public static readonly BlackboardTypeDescriptor EntityId = Create<AIBT.EntityId>(BlackboardValueType.EntityId, "EntityId");
        public static readonly BlackboardTypeDescriptor OperationId = Create<AIBT.OperationId>(BlackboardValueType.OperationId, "OperationId");
        public static readonly BlackboardTypeDescriptor AssetId = Create<AIBT.AssetId>(BlackboardValueType.AssetId, "AssetId");

        public static bool TryGet(BlackboardValueType valueType, out BlackboardTypeDescriptor descriptor)
        {
            switch (valueType)
            {
                case BlackboardValueType.Bool: descriptor = Bool; return true;
                case BlackboardValueType.Int32: descriptor = Int32; return true;
                case BlackboardValueType.Int64: descriptor = Int64; return true;
                case BlackboardValueType.Float32: descriptor = Float32; return true;
                case BlackboardValueType.Float64: descriptor = Float64; return true;
                case BlackboardValueType.Float2: descriptor = Float2; return true;
                case BlackboardValueType.Float3: descriptor = Float3; return true;
                case BlackboardValueType.Quaternion: descriptor = Quaternion; return true;
                case BlackboardValueType.Enum32: descriptor = Enum32; return true;
                case BlackboardValueType.FixedString32: descriptor = FixedString32; return true;
                case BlackboardValueType.FixedString64: descriptor = FixedString64; return true;
                case BlackboardValueType.FixedString128: descriptor = FixedString128; return true;
                case BlackboardValueType.FixedString512: descriptor = FixedString512; return true;
                case BlackboardValueType.AgentId: descriptor = AgentId; return true;
                case BlackboardValueType.EntityId: descriptor = EntityId; return true;
                case BlackboardValueType.OperationId: descriptor = OperationId; return true;
                case BlackboardValueType.AssetId: descriptor = AssetId; return true;
                default:
                    descriptor = default;
                    return false;
            }
        }

        private static BlackboardTypeDescriptor Create<T>(BlackboardValueType valueType, string canonicalTypeId)
            where T : unmanaged
        {
            return new BlackboardTypeDescriptor(
                valueType,
                StableHash.Fnv1A64(canonicalTypeId),
                1,
                UnsafeUtility.SizeOf<T>(),
                UnsafeUtility.AlignOf<T>());
        }
    }
}
