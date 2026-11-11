using System;

namespace AIBT
{
    /// <summary>
    /// A node parameter that names a blackboard key of type <typeparamref name="T"/>. Declare it
    /// as a public field on a node; the tree author chooses the key in the editor and the node
    /// reads or writes it with <c>Get(key)</c> / <c>Set(key, value)</c>.
    /// Supported types: bool, int, long, float, double, Vector2, Vector3, Quaternion and string.
    /// </summary>
    public struct BbKey<T> : IBbKeyBinding
    {
        private string _name;
        private uint _slot;
        private bool _bound;

        /// <summary>The blackboard key this parameter is bound to, or null when it is unbound.</summary>
        public string Name => _name;

        /// <summary>True once a tree has bound this parameter to a declared blackboard key.</summary>
        public bool IsBound => _bound;

        internal uint Slot => _slot;

        void IBbKeyBinding.Bind(string name, uint slot)
        {
            _name = name;
            _slot = slot;
            _bound = true;
        }

        Type IBbKeyBinding.ValueType => typeof(T);

        public override string ToString() => _bound ? _name : "(unbound)";
    }

    internal interface IBbKeyBinding
    {
        Type ValueType { get; }

        void Bind(string name, uint slot);
    }
}
