using System;
using System.Globalization;

namespace AIBT
{
    public readonly struct RuntimeNodeIndex : IEquatable<RuntimeNodeIndex>
    {
        public const uint InvalidValue = uint.MaxValue;

        public RuntimeNodeIndex(uint value)
        {
            Value = value;
        }

        public uint Value { get; }

        public bool IsValid => Value != InvalidValue;

        public static RuntimeNodeIndex Invalid => new RuntimeNodeIndex(InvalidValue);

        public static RuntimeNodeIndex Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException("Runtime node indices must be unsigned decimal values.");
            }

            return result;
        }

        public static bool TryParse(string value, out RuntimeNodeIndex result)
        {
            if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                result = new RuntimeNodeIndex(parsed);
                return true;
            }

            result = Invalid;
            return false;
        }

        public bool Equals(RuntimeNodeIndex other) => Value == other.Value;

        public override bool Equals(object obj) => obj is RuntimeNodeIndex other && Equals(other);

        public override int GetHashCode() => (int)Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(RuntimeNodeIndex left, RuntimeNodeIndex right) => left.Equals(right);

        public static bool operator !=(RuntimeNodeIndex left, RuntimeNodeIndex right) => !left.Equals(right);
    }
}
