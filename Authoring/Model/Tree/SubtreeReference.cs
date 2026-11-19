using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    /// <summary>
    /// The node-level <c>subtree</c> object of a tree format version 3 document (ADR-P11-001 §4.1):
    /// "put the tree <see cref="TreeId"/> here", written for interface version
    /// <see cref="InterfaceVersion"/>, with one binding per interface key. Bindings are held in
    /// ordinal order of their interface key; an interface key may be bound once.
    /// </summary>
    public sealed class SubtreeReference : IEquatable<SubtreeReference>
    {
        private readonly ReadOnlyCollection<SubtreeKeyBinding> _keys;

        public SubtreeReference(TreeId treeId, uint interfaceVersion, IEnumerable<SubtreeKeyBinding> keys = null)
        {
            var copy = new List<SubtreeKeyBinding>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (keys != null)
            {
                foreach (var binding in keys)
                {
                    if (binding == null) throw new ArgumentException("Subtree key bindings cannot be null.", nameof(keys));
                    if (!seen.Add(binding.InterfaceKey))
                        throw new ArgumentException("An interface key can be bound only once.", nameof(keys));
                    copy.Add(binding);
                }
            }

            copy.Sort((left, right) => string.CompareOrdinal(left.InterfaceKey, right.InterfaceKey));
            TreeId = treeId;
            InterfaceVersion = interfaceVersion;
            _keys = copy.AsReadOnly();
        }

        /// <summary>The referenced tree's ID; resolved through an <see cref="ISubtreeResolver"/>.</summary>
        public TreeId TreeId { get; }

        /// <summary>The subtree interface version this reference was written for; at least 1.</summary>
        public uint InterfaceVersion { get; }

        /// <summary>One entry per interface key, in ascending ordinal order of the interface key.</summary>
        public IReadOnlyList<SubtreeKeyBinding> Keys => _keys;

        public bool TryGetBinding(string interfaceKey, out SubtreeKeyBinding binding)
        {
            for (var index = 0; index < _keys.Count; index++)
            {
                if (string.Equals(_keys[index].InterfaceKey, interfaceKey, StringComparison.Ordinal))
                {
                    binding = _keys[index];
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public bool Equals(SubtreeReference other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || TreeId != other.TreeId || InterfaceVersion != other.InterfaceVersion
                || _keys.Count != other._keys.Count) return false;
            for (var index = 0; index < _keys.Count; index++)
                if (!_keys[index].Equals(other._keys[index])) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SubtreeReference);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (TreeId.GetHashCode() * 31) + (int)InterfaceVersion;
                for (var index = 0; index < _keys.Count; index++)
                    hash = (hash * 31) + _keys[index].GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// One entry of a reference's <c>keys</c> map: the subtree interface key and either the parent
    /// Tree-scope key it connects to (written as a string) or a constant (written as
    /// <c>{ "value": v }</c>). A constant keeps its JSON form; it is checked against the interface
    /// key's type when the reference is expanded.
    /// </summary>
    public sealed class SubtreeKeyBinding : IEquatable<SubtreeKeyBinding>
    {
        private SubtreeKeyBinding(string interfaceKey, string parentKey, SemanticValue constant)
        {
            InterfaceKey = interfaceKey ?? throw new ArgumentNullException(nameof(interfaceKey));
            ParentKey = parentKey;
            Constant = constant;
        }

        public static SubtreeKeyBinding ToParentKey(string interfaceKey, string parentKey)
            => new SubtreeKeyBinding(interfaceKey, parentKey ?? throw new ArgumentNullException(nameof(parentKey)), null);

        public static SubtreeKeyBinding ToConstant(string interfaceKey, SemanticValue value)
            => new SubtreeKeyBinding(interfaceKey, null, value ?? throw new ArgumentNullException(nameof(value)));

        public string InterfaceKey { get; }

        /// <summary>The parent blackboard key ID; null for a constant binding.</summary>
        public string ParentKey { get; }

        /// <summary>The constant value; null for a parent-key binding.</summary>
        public SemanticValue Constant { get; }

        public bool IsConstant => Constant != null;

        public bool Equals(SubtreeKeyBinding other)
        {
            if (ReferenceEquals(this, other)) return true;
            return other != null
                && string.Equals(InterfaceKey, other.InterfaceKey, StringComparison.Ordinal)
                && string.Equals(ParentKey, other.ParentKey, StringComparison.Ordinal)
                && Equals(Constant, other.Constant);
        }

        public override bool Equals(object obj) => Equals(obj as SubtreeKeyBinding);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(InterfaceKey);
                hash = (hash * 31) + (ParentKey == null ? 0 : StringComparer.Ordinal.GetHashCode(ParentKey));
                return (hash * 31) + (Constant == null ? 0 : Constant.GetHashCode());
            }
        }
    }
}
