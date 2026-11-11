using System;
using System.Collections.Generic;
using AIBT.Agents;

namespace AIBT
{
    /// <summary>
    /// An agent's blackboard, addressed by the key names declared in its tree. Read and write it
    /// from any script on the main thread: a write made between ticks is seen by the tree on its
    /// next tick, and a write made by a node is seen by the nodes that run after it.
    /// Supported value types: bool, int, long, float, double, Vector2, Vector3, Quaternion and
    /// string. The value type must match the key's declared type.
    /// </summary>
    public sealed class AgentBlackboard
    {
        private readonly ManagedTreeInstance _instance;

        internal AgentBlackboard(ManagedTreeInstance instance)
        {
            _instance = instance;
        }

        /// <summary>The key names this tree declares.</summary>
        public IReadOnlyList<string> Keys => _instance.KeyNames;

        /// <summary>Whether the tree declares <paramref name="key"/>.</summary>
        public bool Has(string key) => _instance.TryResolveKey(key, out _, out _);

        /// <summary>Reads a value. Throws when the key is undeclared or has another type.</summary>
        public T Get<T>(string key)
        {
            if (!TryGet<T>(key, out var value)) throw Mismatch<T>(key);
            return value;
        }

        /// <summary>Reads a value, or returns <paramref name="fallback"/> when the key is undeclared or has another type.</summary>
        public T GetOrDefault<T>(string key, T fallback = default)
            => TryGet<T>(key, out var value) ? value : fallback;

        /// <summary>Reads a value without throwing.</summary>
        public bool TryGet<T>(string key, out T value)
        {
            value = default;
            return BbCodec<T>.Supported
                && _instance.TryResolveKey(key, out var slot, out var slotType)
                && BbTypes.Accepts(typeof(T), slotType)
                && _instance.TryRead(slot, out var raw)
                && BbCodec<T>.Decode(raw, out value);
        }

        /// <summary>Writes a value. Throws when the key is undeclared or has another type.</summary>
        public void Set<T>(string key, T value)
        {
            if (!TrySet(key, value)) throw Mismatch<T>(key);
        }

        /// <summary>Writes a value without throwing.</summary>
        public bool TrySet<T>(string key, T value)
        {
            if (!BbCodec<T>.Supported
                || !_instance.TryResolveKey(key, out var slot, out var slotType)
                || !BbTypes.Accepts(typeof(T), slotType))
            {
                return false;
            }

            return Write(slot, slotType, value);
        }

        internal T Get<T>(BbKey<T> key, string nodeId)
        {
            if (!key.IsBound) throw Unbound(nodeId);
            if (!_instance.TryRead(key.Slot, out var raw) || !BbCodec<T>.Decode(raw, out var value))
            {
                throw new InvalidOperationException("Node '" + nodeId + "' could not read blackboard key '" + key.Name + "'.");
            }

            return value;
        }

        internal void Set<T>(BbKey<T> key, T value, string nodeId)
        {
            if (!key.IsBound) throw Unbound(nodeId);
            if (!Write(key.Slot, _instance.SlotType(key.Slot), value))
            {
                throw new InvalidOperationException("Node '" + nodeId + "' could not write blackboard key '" + key.Name + "'.");
            }
        }

        private bool Write<T>(uint slot, BlackboardValueType slotType, T value)
        {
            BlackboardValue encoded;
            try
            {
                encoded = BbCodec<T>.Encode(value, slotType);
            }
            catch (ArgumentException)
            {
                // NaN/infinite numbers and over-long strings are rejected by the value contract.
                return false;
            }

            return encoded.IsValid && _instance.TryWrite(slot, encoded);
        }

        private Exception Mismatch<T>(string key)
        {
            if (!BbCodec<T>.Supported)
            {
                return new NotSupportedException("Blackboard values of type " + typeof(T).Name
                    + " are not supported. Use bool, int, long, float, double, Vector2, Vector3, Quaternion or string.");
            }

            if (!_instance.TryResolveKey(key, out _, out var slotType))
            {
                return new KeyNotFoundException("The behavior tree does not declare blackboard key '" + key + "'.");
            }

            if (!BbTypes.Accepts(typeof(T), slotType))
            {
                return new InvalidOperationException("Blackboard key '" + key + "' is " + slotType + ", not " + BbTypes.Describe(typeof(T)) + ".");
            }

            return new InvalidOperationException("Blackboard key '" + key + "' rejected the value (not a finite number, too long, or the tree is mid-update).");
        }

        private static Exception Unbound(string nodeId)
            => new InvalidOperationException("Node '" + nodeId + "' uses a blackboard key parameter that the tree leaves unbound.");
    }
}
