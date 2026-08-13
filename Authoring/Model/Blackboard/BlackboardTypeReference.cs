using System;

namespace AIBT.Authoring
{
    public readonly struct BlackboardTypeReference : IEquatable<BlackboardTypeReference>
    {
        private BlackboardTypeReference(
            string canonicalTypeId,
            BlackboardTypeDescriptor runtimeDescriptor,
            RegisteredUnmanagedTypeDescriptor registeredDescriptor,
            string enumContract = null)
        {
            CanonicalTypeId = canonicalTypeId;
            RuntimeDescriptor = runtimeDescriptor;
            RegisteredDescriptor = registeredDescriptor;
            EnumContract = enumContract;
            EnumContractId = enumContract == null ? 0 : StableHash.Fnv1A64(enumContract);
        }

        public string CanonicalTypeId { get; }

        public BlackboardTypeDescriptor RuntimeDescriptor { get; }

        public RegisteredUnmanagedTypeDescriptor RegisteredDescriptor { get; }

        public string EnumContract { get; }

        public ulong EnumContractId { get; }

        public BlackboardValueType ValueType => RuntimeDescriptor.ValueType;

        public bool IsRegistered => ValueType == BlackboardValueType.Registered;

        public bool IsValid => !string.IsNullOrEmpty(CanonicalTypeId)
            && RuntimeDescriptor.IsValid
            && (ValueType == BlackboardValueType.Enum32
                ? !string.IsNullOrWhiteSpace(EnumContract) && EnumContractId != 0
                : EnumContract == null && EnumContractId == 0);

        public static BlackboardTypeReference BuiltIn(BlackboardValueType valueType)
        {
            if (valueType == BlackboardValueType.Enum32)
            {
                throw new ArgumentException(
                    "Enum32 requires a canonical enum contract. Use BlackboardTypeReference.Enum32(contract).",
                    nameof(valueType));
            }

            if (!BuiltInBlackboardTypes.TryGet(valueType, out var descriptor))
            {
                throw new ArgumentOutOfRangeException(nameof(valueType), "A built-in value type is required.");
            }

            return new BlackboardTypeReference(GetCanonicalBuiltInName(valueType), descriptor, default);
        }

        public static BlackboardTypeReference Enum32(string canonicalContract)
        {
            if (string.IsNullOrWhiteSpace(canonicalContract))
            {
                throw new ArgumentException("A canonical enum contract is required.", nameof(canonicalContract));
            }

            var contractId = StableHash.Fnv1A64(canonicalContract);
            if (contractId == 0)
            {
                throw new ArgumentException("The canonical enum contract hashes to the reserved zero identity.", nameof(canonicalContract));
            }

            return new BlackboardTypeReference(
                GetCanonicalBuiltInName(BlackboardValueType.Enum32),
                BuiltInBlackboardTypes.Enum32,
                default,
                canonicalContract);
        }

        public static BlackboardTypeReference Registered(
            string canonicalTypeId,
            RegisteredUnmanagedTypeDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(canonicalTypeId))
            {
                throw new ArgumentException("A canonical registered type ID is required.", nameof(canonicalTypeId));
            }

            if (!descriptor.IsValid)
            {
                throw new ArgumentException("A valid unmanaged descriptor is required.", nameof(descriptor));
            }

            if (StableHash.Fnv1A64(canonicalTypeId) != descriptor.TypeId)
            {
                throw new ArgumentException("The canonical type ID does not match the descriptor's numeric type ID.");
            }

            return new BlackboardTypeReference(
                canonicalTypeId,
                BlackboardTypeDescriptor.FromRegistered(descriptor),
                descriptor);
        }

        public bool Equals(BlackboardTypeReference other)
        {
            return string.Equals(CanonicalTypeId, other.CanonicalTypeId, StringComparison.Ordinal)
                && RuntimeDescriptor == other.RuntimeDescriptor
                && RegisteredDescriptor == other.RegisteredDescriptor
                && string.Equals(EnumContract, other.EnumContract, StringComparison.Ordinal)
                && EnumContractId == other.EnumContractId;
        }

        public override bool Equals(object obj) => obj is BlackboardTypeReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = CanonicalTypeId == null ? 0 : StringComparer.Ordinal.GetHashCode(CanonicalTypeId);
                hashCode = (hashCode * 397) ^ RuntimeDescriptor.GetHashCode();
                hashCode = (hashCode * 397) ^ RegisteredDescriptor.GetHashCode();
                hashCode = (hashCode * 397) ^ (EnumContract == null ? 0 : StringComparer.Ordinal.GetHashCode(EnumContract));
                return (hashCode * 397) ^ EnumContractId.GetHashCode();
            }
        }

        public override string ToString() => CanonicalTypeId ?? string.Empty;

        public static bool operator ==(BlackboardTypeReference left, BlackboardTypeReference right)
            => left.Equals(right);

        public static bool operator !=(BlackboardTypeReference left, BlackboardTypeReference right)
            => !left.Equals(right);

        private static string GetCanonicalBuiltInName(BlackboardValueType valueType)
        {
            switch (valueType)
            {
                case BlackboardValueType.Bool: return "Bool";
                case BlackboardValueType.Int32: return "Int32";
                case BlackboardValueType.Int64: return "Int64";
                case BlackboardValueType.Float32: return "Float32";
                case BlackboardValueType.Float64: return "Float64";
                case BlackboardValueType.Float2: return "Float2";
                case BlackboardValueType.Float3: return "Float3";
                case BlackboardValueType.Quaternion: return "Quaternion";
                case BlackboardValueType.Enum32: return "Enum32";
                case BlackboardValueType.FixedString32: return "FixedString32";
                case BlackboardValueType.FixedString64: return "FixedString64";
                case BlackboardValueType.FixedString128: return "FixedString128";
                case BlackboardValueType.FixedString512: return "FixedString512";
                case BlackboardValueType.AgentId: return "AgentId";
                case BlackboardValueType.EntityId: return "EntityId";
                case BlackboardValueType.OperationId: return "OperationId";
                case BlackboardValueType.AssetId: return "AssetId";
                default: throw new ArgumentOutOfRangeException(nameof(valueType));
            }
        }
    }
}
