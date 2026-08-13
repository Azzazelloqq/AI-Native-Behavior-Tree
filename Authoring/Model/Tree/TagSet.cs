using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class TagSet : IEquatable<TagSet>
    {
        private readonly ReadOnlyCollection<string> _values;

        public TagSet(IEnumerable<string> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var unique = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (!unique.Add(value))
                {
                    HasDuplicateValues = true;
                }
            }

            _values = new List<string>(unique).AsReadOnly();
        }

        public static TagSet Empty { get; } = new TagSet(Array.Empty<string>());

        public IReadOnlyList<string> Values => _values;

        public bool HasDuplicateValues { get; }

        public bool Contains(string value)
        {
            var lower = 0;
            var upper = _values.Count - 1;
            while (lower <= upper)
            {
                var middle = lower + ((upper - lower) / 2);
                var comparison = StringComparer.Ordinal.Compare(_values[middle], value);
                if (comparison == 0)
                {
                    return true;
                }

                if (comparison < 0)
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle - 1;
                }
            }

            return false;
        }

        public bool Equals(TagSet other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null
                || HasDuplicateValues != other.HasDuplicateValues
                || _values.Count != other._values.Count)
            {
                return false;
            }

            for (var index = 0; index < _values.Count; index++)
            {
                if (!string.Equals(_values[index], other._values[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as TagSet);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = HasDuplicateValues ? 1 : 0;
                for (var index = 0; index < _values.Count; index++)
                {
                    hash = (hash * 31) + (_values[index] == null ? 0 : StringComparer.Ordinal.GetHashCode(_values[index]));
                }

                return hash;
            }
        }
    }
}
