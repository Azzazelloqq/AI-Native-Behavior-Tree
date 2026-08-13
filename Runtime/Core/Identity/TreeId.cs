using System;

namespace AIBT
{
    public readonly struct TreeId : IEquatable<TreeId>
    {
        public TreeId(string value)
        {
            Value = AuthoringId.Parse(value, nameof(value));
        }

        public string Value { get; }

        public bool IsValid => AuthoringId.IsValid(Value);

        public static TreeId Parse(string value)
        {
            return new TreeId(value);
        }

        public static bool TryParse(string value, out TreeId result)
        {
            if (!AuthoringId.IsValid(value))
            {
                result = default;
                return false;
            }

            result = new TreeId(value);
            return true;
        }

        public bool Equals(TreeId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TreeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(TreeId left, TreeId right) => left.Equals(right);

        public static bool operator !=(TreeId left, TreeId right) => !left.Equals(right);
    }
}
