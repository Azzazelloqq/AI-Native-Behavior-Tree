using System;
using System.Globalization;

namespace AIBT
{
    public readonly struct Revision : IEquatable<Revision>
    {
        public Revision(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool IsValid => Value != 0;

        public static Revision Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException("Revisions must be unsigned decimal values.");
            }

            return result;
        }

        public static bool TryParse(string value, out Revision result)
        {
            if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                result = new Revision(parsed);
                return true;
            }

            result = default;
            return false;
        }

        public bool Equals(Revision other) => Value == other.Value;

        public override bool Equals(object obj) => obj is Revision other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(Revision left, Revision right) => left.Equals(right);

        public static bool operator !=(Revision left, Revision right) => !left.Equals(right);
    }
}
