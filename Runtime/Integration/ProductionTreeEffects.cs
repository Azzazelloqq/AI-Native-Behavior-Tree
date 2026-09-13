using System;

namespace AIBT
{
    /// <summary>Closed payload kinds the first production-world bridge exposes to Unity code.</summary>
    public enum ProductionTreeEffectValueKind : byte
    {
        None = 0,
        Int32 = 1,
        Float32 = 2,
        Float3 = 3,
    }

    /// <summary>
    /// Source-generated identity and payload shape of one declared <c>EffectCommand</c>. It never
    /// exposes a native buffer, command ordinal, or payload offset.
    /// </summary>
    public readonly struct ProductionTreeEffectDescriptor : IEquatable<ProductionTreeEffectDescriptor>
    {
        public ProductionTreeEffectDescriptor(ulong stableId, ProductionTreeEffectValueKind valueKind)
        {
            StableId = stableId;
            ValueKind = valueKind;
        }

        public ulong StableId { get; }
        public ProductionTreeEffectValueKind ValueKind { get; }
        public bool IsSupported => StableId != 0 && ValueKind != ProductionTreeEffectValueKind.None;

        public bool Equals(ProductionTreeEffectDescriptor other)
            => StableId == other.StableId && ValueKind == other.ValueKind;
        public override bool Equals(object obj) => obj is ProductionTreeEffectDescriptor other && Equals(other);
        public override int GetHashCode() => StableId.GetHashCode() ^ (int)ValueKind;
        public static bool operator ==(ProductionTreeEffectDescriptor left, ProductionTreeEffectDescriptor right) => left.Equals(right);
        public static bool operator !=(ProductionTreeEffectDescriptor left, ProductionTreeEffectDescriptor right) => !left.Equals(right);
    }

    /// <summary>A committed, main-thread consumable behavior-tree effect.</summary>
    public readonly struct ProductionTreeEffect
    {
        private readonly ProductionTreeEffectDescriptor _descriptor;
        private readonly int _int32;
        private readonly float _float32;
        private readonly Float3Value _float3;

        internal ProductionTreeEffect(ProductionTreeEffectDescriptor descriptor, int value)
        {
            _descriptor = descriptor;
            _int32 = value;
            _float32 = default;
            _float3 = default;
        }

        internal ProductionTreeEffect(ProductionTreeEffectDescriptor descriptor, float value)
        {
            _descriptor = descriptor;
            _int32 = default;
            _float32 = value;
            _float3 = default;
        }

        internal ProductionTreeEffect(ProductionTreeEffectDescriptor descriptor, Float3Value value)
        {
            _descriptor = descriptor;
            _int32 = default;
            _float32 = default;
            _float3 = value;
        }

        public ProductionTreeEffectDescriptor Descriptor => _descriptor;
        public ulong StableId => _descriptor.StableId;
        public ProductionTreeEffectValueKind ValueKind => _descriptor.ValueKind;

        public bool TryGetInt32(out int value)
        {
            value = _int32;
            return ValueKind == ProductionTreeEffectValueKind.Int32;
        }

        public bool TryGetFloat32(out float value)
        {
            value = _float32;
            return ValueKind == ProductionTreeEffectValueKind.Float32;
        }

        public bool TryGetFloat3(out Float3Value value)
        {
            value = _float3;
            return ValueKind == ProductionTreeEffectValueKind.Float3;
        }
    }

    /// <summary>Receives committed behavior-tree effects on Unity's main thread.</summary>
    public interface IProductionTreeEffectReceiver
    {
        void Receive(in ProductionTreeEffect effect);
    }

    internal sealed class ProductionTreeEffectQueue
    {
        private readonly ProductionTreeEffect[] _items;
        private int _head;
        private int _count;

        internal ProductionTreeEffectQueue(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new ProductionTreeEffect[capacity];
        }

        internal int Count => _count;
        internal int Free => _items.Length - _count;

        internal bool CanAccept(int count) => count >= 0 && count <= Free;

        internal bool TryEnqueue(in ProductionTreeEffect effect)
        {
            if (_count == _items.Length) return false;
            var index = _head + _count;
            if (index >= _items.Length) index -= _items.Length;
            _items[index] = effect;
            _count++;
            return true;
        }

        internal bool TryPeek(out ProductionTreeEffect effect)
        {
            effect = default;
            if (_count == 0) return false;
            effect = _items[_head];
            return true;
        }

        internal void RemoveHead()
        {
            if (_count == 0) return;
            _items[_head] = default;
            _head++;
            if (_head == _items.Length) _head = 0;
            _count--;
        }
    }
}
