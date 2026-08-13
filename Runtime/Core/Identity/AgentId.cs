using System;
using System.Globalization;

namespace AIBT
{
    public readonly struct AgentId : IEquatable<AgentId>
    {
        public AgentId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool IsValid => Value != 0;

        public static AgentId Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException("Agent IDs must be unsigned decimal values.");
            }

            return result;
        }

        public static bool TryParse(string value, out AgentId result)
        {
            if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                result = new AgentId(parsed);
                return true;
            }

            result = default;
            return false;
        }

        public bool Equals(AgentId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is AgentId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(AgentId left, AgentId right) => left.Equals(right);

        public static bool operator !=(AgentId left, AgentId right) => !left.Equals(right);
    }
}
