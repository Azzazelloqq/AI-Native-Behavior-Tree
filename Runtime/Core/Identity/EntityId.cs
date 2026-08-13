using System;
using System.Globalization;

namespace AIBT
{
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public EntityId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool IsValid => Value != 0;

        public static EntityId Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException("Entity IDs must be unsigned decimal values.");
            }

            return result;
        }

        public static bool TryParse(string value, out EntityId result)
        {
            if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                result = new EntityId(parsed);
                return true;
            }

            result = default;
            return false;
        }

        public bool Equals(EntityId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
