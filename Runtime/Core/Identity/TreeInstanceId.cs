using System;
using System.Globalization;

namespace AIBT
{
    public readonly struct TreeInstanceId : IEquatable<TreeInstanceId>, IComparable<TreeInstanceId>
    {
        public TreeInstanceId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool IsValid => Value != 0;

        public static TreeInstanceId Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException("Tree instance IDs must be unsigned decimal values.");
            }

            return result;
        }

        public static bool TryParse(string value, out TreeInstanceId result)
        {
            if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                result = new TreeInstanceId(parsed);
                return true;
            }

            result = default;
            return false;
        }

        public int CompareTo(TreeInstanceId other) => Value.CompareTo(other.Value);

        public bool Equals(TreeInstanceId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is TreeInstanceId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(TreeInstanceId left, TreeInstanceId right) => left.Equals(right);

        public static bool operator !=(TreeInstanceId left, TreeInstanceId right) => !left.Equals(right);

        public static bool operator <(TreeInstanceId left, TreeInstanceId right) => left.CompareTo(right) < 0;

        public static bool operator >(TreeInstanceId left, TreeInstanceId right) => left.CompareTo(right) > 0;

        public static bool operator <=(TreeInstanceId left, TreeInstanceId right) => left.CompareTo(right) <= 0;

        public static bool operator >=(TreeInstanceId left, TreeInstanceId right) => left.CompareTo(right) >= 0;
    }
}
