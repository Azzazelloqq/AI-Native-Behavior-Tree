using System;

namespace AIBT
{
    public readonly struct Enum32Value : IEquatable<Enum32Value>
    {
        public Enum32Value(ulong contractTypeId, int value)
        {
            if (contractTypeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contractTypeId));
            }

            ContractTypeId = contractTypeId;
            Value = value;
        }

        public ulong ContractTypeId { get; }

        public int Value { get; }

        public bool IsValid => ContractTypeId != 0;

        public bool Equals(Enum32Value other)
            => ContractTypeId == other.ContractTypeId && Value == other.Value;

        public override bool Equals(object obj) => obj is Enum32Value other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (ContractTypeId.GetHashCode() * 397) ^ Value;
            }
        }

        public static bool operator ==(Enum32Value left, Enum32Value right) => left.Equals(right);

        public static bool operator !=(Enum32Value left, Enum32Value right) => !left.Equals(right);
    }
}
