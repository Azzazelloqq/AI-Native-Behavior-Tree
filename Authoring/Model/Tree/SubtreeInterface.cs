using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    /// <summary>
    /// The top-level <c>interface</c> of a tree format version 3 document (ADR-P11-001 §4.3): the
    /// Tree-scope keys every caller of this tree as a subtree must connect, and the interface
    /// version a reference states. Keys are held in ordinal order; duplicates are kept so the
    /// validator can report them (<c>AIBT2050</c>).
    /// </summary>
    public sealed class SubtreeInterface : IEquatable<SubtreeInterface>
    {
        private readonly ReadOnlyCollection<string> _keys;

        public SubtreeInterface(uint version, IEnumerable<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            var copy = new List<string>();
            foreach (var key in keys)
            {
                if (key == null) throw new ArgumentException("Interface keys cannot be null.", nameof(keys));
                copy.Add(key);
            }

            copy.Sort(StringComparer.Ordinal);
            Version = version;
            _keys = copy.AsReadOnly();
        }

        /// <summary>Raised by the author when the interface changes incompatibly; at least 1.</summary>
        public uint Version { get; }

        /// <summary>Tree-scope blackboard key IDs, in ascending ordinal order.</summary>
        public IReadOnlyList<string> Keys => _keys;

        public bool Equals(SubtreeInterface other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || Version != other.Version || _keys.Count != other._keys.Count) return false;
            for (var index = 0; index < _keys.Count; index++)
                if (!string.Equals(_keys[index], other._keys[index], StringComparison.Ordinal)) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SubtreeInterface);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Version;
                for (var index = 0; index < _keys.Count; index++)
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_keys[index]);
                return hash;
            }
        }
    }
}
