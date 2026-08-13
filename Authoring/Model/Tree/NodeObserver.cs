using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class NodeObserver : IEquatable<NodeObserver>
    {
        private readonly ReadOnlyCollection<string> _watchedKeys;

        public NodeObserver(string mode, IEnumerable<string> watchedKeys)
        {
            if (watchedKeys == null)
            {
                throw new ArgumentNullException(nameof(watchedKeys));
            }

            Mode = mode;
            _watchedKeys = new List<string>(watchedKeys).AsReadOnly();
        }

        public string Mode { get; }

        public IReadOnlyList<string> WatchedKeys => _watchedKeys;

        public bool Equals(NodeObserver other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null
                || !string.Equals(Mode, other.Mode, StringComparison.Ordinal)
                || _watchedKeys.Count != other._watchedKeys.Count)
            {
                return false;
            }

            for (var index = 0; index < _watchedKeys.Count; index++)
            {
                if (!string.Equals(_watchedKeys[index], other._watchedKeys[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as NodeObserver);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Mode == null ? 0 : StringComparer.Ordinal.GetHashCode(Mode);
                for (var index = 0; index < _watchedKeys.Count; index++)
                {
                    hash = (hash * 31) + (_watchedKeys[index] == null ? 0 : StringComparer.Ordinal.GetHashCode(_watchedKeys[index]));
                }

                return hash;
            }
        }
    }
}
