using System;
using System.Collections.Generic;

namespace AIBT
{
    internal enum ReferenceReactiveCompositeKind : byte
    {
        Sequence = 1,
        Selector = 2,
    }

    internal interface IReferenceReactiveCompositeHandler : IReferenceMemoryCompositeHandler
    {
    }

    internal readonly struct ReferenceReactiveCompositeBinding
    {
        internal ReferenceReactiveCompositeBinding(
            ulong nodeTypeId,
            uint nodeTypeVersion,
            IReferenceReactiveCompositeHandler handler,
            ReferenceReactiveCompositeKind kind)
        {
            if (nodeTypeId == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            if (nodeTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeVersion));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            if (!Enum.IsDefined(typeof(ReferenceReactiveCompositeKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
            Kind = kind;
        }

        internal ulong NodeTypeId { get; }
        internal uint NodeTypeVersion { get; }
        internal IReferenceReactiveCompositeHandler Handler { get; }
        internal ReferenceReactiveCompositeKind Kind { get; }
    }

    internal sealed class ReferenceReactiveCompositeRegistry
    {
        private readonly Dictionary<HandlerKey, Entry> _handlers;

        internal ReferenceReactiveCompositeRegistry(IEnumerable<ReferenceReactiveCompositeBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _handlers = new Dictionary<HandlerKey, Entry>();
            foreach (var binding in bindings)
            {
                var key = new HandlerKey(binding.NodeTypeId, binding.NodeTypeVersion);
                if (_handlers.ContainsKey(key))
                {
                    throw new ArgumentException("Reactive-composite bindings must be unique by numeric type ID and version.", nameof(bindings));
                }

                _handlers.Add(key, new Entry(binding.Handler, binding.Kind));
            }
        }

        internal static ReferenceReactiveCompositeRegistry Empty { get; }
            = new ReferenceReactiveCompositeRegistry(Array.Empty<ReferenceReactiveCompositeBinding>());

        internal static ReferenceReactiveCompositeRegistry CreatePhase1BuiltIns()
        {
            return new ReferenceReactiveCompositeRegistry(new[]
            {
                new ReferenceReactiveCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.reactive-sequence"),
                    1,
                    new ReferenceReactiveSequenceHandler(),
                    ReferenceReactiveCompositeKind.Sequence),
                new ReferenceReactiveCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.reactive-selector"),
                    1,
                    new ReferenceReactiveSelectorHandler(),
                    ReferenceReactiveCompositeKind.Selector),
            });
        }

        internal bool TryGet(ulong nodeTypeId, uint nodeTypeVersion, out IReferenceReactiveCompositeHandler handler)
        {
            if (_handlers.TryGetValue(new HandlerKey(nodeTypeId, nodeTypeVersion), out var entry))
            {
                handler = entry.Handler;
                return true;
            }
            handler = null;
            return false;
        }

        internal bool TryGetKind(ulong nodeTypeId, uint nodeTypeVersion, out ReferenceReactiveCompositeKind kind)
        {
            if (_handlers.TryGetValue(new HandlerKey(nodeTypeId, nodeTypeVersion), out var entry))
            {
                kind = entry.Kind;
                return true;
            }
            kind = default;
            return false;
        }

        private readonly struct Entry
        {
            internal Entry(IReferenceReactiveCompositeHandler handler, ReferenceReactiveCompositeKind kind)
            {
                Handler = handler;
                Kind = kind;
            }
            internal IReferenceReactiveCompositeHandler Handler { get; }
            internal ReferenceReactiveCompositeKind Kind { get; }
        }

        private readonly struct HandlerKey : IEquatable<HandlerKey>
        {
            internal HandlerKey(ulong typeId, uint version)
            {
                TypeId = typeId;
                Version = version;
            }

            private ulong TypeId { get; }
            private uint Version { get; }
            public bool Equals(HandlerKey other) => TypeId == other.TypeId && Version == other.Version;
            public override bool Equals(object obj) => obj is HandlerKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (TypeId.GetHashCode() * 397) ^ (int)Version; }
            }
        }
    }

    internal sealed class ReferenceReactiveSequenceHandler : IReferenceReactiveCompositeHandler
    {
        private readonly ReferenceMemorySequenceHandler _inner = new ReferenceMemorySequenceHandler();
        public NodeStatus EmptyResult => _inner.EmptyResult;
        public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
            => _inner.Accept(childResult, childCursor, childCount);
    }

    internal sealed class ReferenceReactiveSelectorHandler : IReferenceReactiveCompositeHandler
    {
        private readonly ReferenceMemorySelectorHandler _inner = new ReferenceMemorySelectorHandler();
        public NodeStatus EmptyResult => _inner.EmptyResult;
        public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
            => _inner.Accept(childResult, childCursor, childCount);
    }
}
