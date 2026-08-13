using System;

namespace AIBT
{
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public NodeId(string value)
        {
            Value = AuthoringId.Parse(value, nameof(value));
        }

        public string Value { get; }

        public bool IsValid => AuthoringId.IsValid(Value);

        public static NodeId Parse(string value)
        {
            return new NodeId(value);
        }

        public static bool TryParse(string value, out NodeId result)
        {
            if (!AuthoringId.IsValid(value))
            {
                result = default;
                return false;
            }

            result = new NodeId(value);
            return true;
        }

        public bool Equals(NodeId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NodeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);

        public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);
    }
}
