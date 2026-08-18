using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class NodeBindingMap : IEquatable<NodeBindingMap>
    {
        private readonly ReadOnlyDictionary<string, string> _values;

        public NodeBindingMap(IEnumerable<KeyValuePair<string, string>> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                if (!GeneratedIdentityRules.IsValidMemberId(pair.Key))
                    throw new ArgumentException("Binding IDs must use the canonical member identity grammar.", nameof(values));
                if (!GeneratedIdentityRules.IsValidMemberId(pair.Value))
                    throw new ArgumentException("Blackboard key IDs must use the canonical member identity grammar.", nameof(values));
                if (copy.ContainsKey(pair.Key))
                    throw new ArgumentException("Binding IDs must be unique.", nameof(values));
                copy.Add(pair.Key, pair.Value);
            }
            _values = new ReadOnlyDictionary<string, string>(copy);
        }

        public IReadOnlyDictionary<string, string> Values => _values;

        public bool Equals(NodeBindingMap other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || _values.Count != other._values.Count) return false;
            foreach (var pair in _values)
                if (!other._values.TryGetValue(pair.Key, out var value)
                    || !string.Equals(pair.Value, value, StringComparison.Ordinal)) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as NodeBindingMap);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var pair in _values)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(pair.Key);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(pair.Value);
                }
                return hash;
            }
        }
    }

    internal static class GeneratedIdentityRules
    {
        internal static bool IsValidMemberId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 255) return false;
            var segments = value.Split('.');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                if (segment.Length == 0 || segment[0] == '-' || segment[segment.Length - 1] == '-') return false;
                for (var characterIndex = 0; characterIndex < segment.Length; characterIndex++)
                {
                    var character = segment[characterIndex];
                    if ((character < 'a' || character > 'z')
                        && (character < '0' || character > '9')
                        && character != '-') return false;
                }
            }
            return true;
        }
    }
}
