using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public enum SemanticValueKind : byte
    {
        Null,
        Boolean,
        SignedInteger,
        UnsignedInteger,
        Number,
        String,
        Array,
        Object,
    }

    public sealed class SemanticProperty : IEquatable<SemanticProperty>
    {
        public SemanticProperty(string name, SemanticValue value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public SemanticValue Value { get; }

        public bool Equals(SemanticProperty other)
        {
            return other != null
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && Equals(Value, other.Value);
        }

        public override bool Equals(object obj) => Equals(obj as SemanticProperty);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name)) * 397)
                    ^ (Value == null ? 0 : Value.GetHashCode());
            }
        }
    }

    public sealed class SemanticObject : IEquatable<SemanticObject>
    {
        private readonly ReadOnlyCollection<SemanticProperty> _properties;

        public SemanticObject(IEnumerable<SemanticProperty> properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            _properties = new List<SemanticProperty>(properties).AsReadOnly();
        }

        public static SemanticObject Empty { get; } = new SemanticObject(Array.Empty<SemanticProperty>());

        public IReadOnlyList<SemanticProperty> Properties => _properties;

        public bool TryGetValue(string name, out SemanticValue value)
        {
            for (var index = 0; index < _properties.Count; index++)
            {
                var property = _properties[index];
                if (property != null && string.Equals(property.Name, name, StringComparison.Ordinal))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public bool Equals(SemanticObject other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null || _properties.Count != other._properties.Count)
            {
                return false;
            }

            for (var index = 0; index < _properties.Count; index++)
            {
                if (!Equals(_properties[index], other._properties[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SemanticObject);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                for (var index = 0; index < _properties.Count; index++)
                {
                    hash = (hash * 31) + (_properties[index] == null ? 0 : _properties[index].GetHashCode());
                }

                return hash;
            }
        }
    }

    public sealed class SemanticValue : IEquatable<SemanticValue>
    {
        private readonly object _value;

        private SemanticValue(SemanticValueKind kind, object value)
        {
            Kind = kind;
            _value = value;
        }

        public static SemanticValue Null { get; } = new SemanticValue(SemanticValueKind.Null, null);

        public SemanticValueKind Kind { get; }

        public static SemanticValue FromBoolean(bool value) => new SemanticValue(SemanticValueKind.Boolean, value);

        public static SemanticValue FromInt64(long value) => new SemanticValue(SemanticValueKind.SignedInteger, value);

        public static SemanticValue FromUInt64(ulong value) => new SemanticValue(SemanticValueKind.UnsignedInteger, value);

        public static SemanticValue FromNumber(double value) => new SemanticValue(SemanticValueKind.Number, value);

        public static SemanticValue FromString(string value) => new SemanticValue(SemanticValueKind.String, value);

        public static SemanticValue FromArray(IEnumerable<SemanticValue> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return new SemanticValue(
                SemanticValueKind.Array,
                new List<SemanticValue>(values).AsReadOnly());
        }

        public static SemanticValue FromObject(SemanticObject value)
        {
            return new SemanticValue(SemanticValueKind.Object, value);
        }

        public bool TryGetBoolean(out bool value) => TryGetValue(SemanticValueKind.Boolean, out value);

        public bool TryGetInt64(out long value) => TryGetValue(SemanticValueKind.SignedInteger, out value);

        public bool TryGetUInt64(out ulong value) => TryGetValue(SemanticValueKind.UnsignedInteger, out value);

        public bool TryGetNumber(out double value) => TryGetValue(SemanticValueKind.Number, out value);

        public bool TryGetString(out string value)
        {
            if (Kind == SemanticValueKind.String)
            {
                value = (string)_value;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetArray(out IReadOnlyList<SemanticValue> values)
        {
            if (Kind == SemanticValueKind.Array)
            {
                values = (IReadOnlyList<SemanticValue>)_value;
                return true;
            }

            values = null;
            return false;
        }

        public bool TryGetObject(out SemanticObject value)
        {
            if (Kind == SemanticValueKind.Object)
            {
                value = (SemanticObject)_value;
                return true;
            }

            value = null;
            return false;
        }

        public bool Equals(SemanticValue other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null || Kind != other.Kind)
            {
                return false;
            }

            if (Kind == SemanticValueKind.Array)
            {
                var left = (IReadOnlyList<SemanticValue>)_value;
                var right = (IReadOnlyList<SemanticValue>)other._value;
                if (left.Count != right.Count)
                {
                    return false;
                }

                for (var index = 0; index < left.Count; index++)
                {
                    if (!Equals(left[index], right[index]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return Equals(_value, other._value);
        }

        public override bool Equals(object obj) => Equals(obj as SemanticValue);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind * 397;
                if (Kind != SemanticValueKind.Array)
                {
                    return hash ^ (_value == null ? 0 : _value.GetHashCode());
                }

                var values = (IReadOnlyList<SemanticValue>)_value;
                for (var index = 0; index < values.Count; index++)
                {
                    hash = (hash * 31) + (values[index] == null ? 0 : values[index].GetHashCode());
                }

                return hash;
            }
        }

        private bool TryGetValue<T>(SemanticValueKind expectedKind, out T value)
        {
            if (Kind == expectedKind)
            {
                value = (T)_value;
                return true;
            }

            value = default;
            return false;
        }
    }
}
