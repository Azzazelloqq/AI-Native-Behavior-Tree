using System;
using System.Collections.Generic;

namespace AIBT
{
    internal readonly struct ReferenceCompositeDecision
    {
        private ReferenceCompositeDecision(bool isTerminal, NodeStatus status, uint cursorAfterAcceptance)
        {
            IsTerminal = isTerminal;
            Status = status;
            CursorAfterAcceptance = cursorAfterAcceptance;
        }

        internal bool IsTerminal { get; }
        internal NodeStatus Status { get; }
        internal uint CursorAfterAcceptance { get; }

        internal static ReferenceCompositeDecision Continue(uint cursorAfterAcceptance)
            => new ReferenceCompositeDecision(false, default, cursorAfterAcceptance);

        internal static ReferenceCompositeDecision Terminal(NodeStatus status, uint cursorAfterAcceptance)
            => new ReferenceCompositeDecision(true, status, cursorAfterAcceptance);
    }

    internal interface IReferenceMemoryCompositeHandler
    {
        NodeStatus EmptyResult { get; }

        ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount);
    }

    internal readonly struct ReferenceMemoryCompositeBinding
    {
        internal ReferenceMemoryCompositeBinding(
            ulong nodeTypeId,
            uint nodeTypeVersion,
            IReferenceMemoryCompositeHandler handler)
        {
            if (nodeTypeId == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            if (nodeTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeVersion));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
        }

        internal ulong NodeTypeId { get; }
        internal uint NodeTypeVersion { get; }
        internal IReferenceMemoryCompositeHandler Handler { get; }
    }

    internal sealed class ReferenceMemoryCompositeRegistry
    {
        private readonly Dictionary<HandlerKey, IReferenceMemoryCompositeHandler> _handlers;

        internal ReferenceMemoryCompositeRegistry(IEnumerable<ReferenceMemoryCompositeBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _handlers = new Dictionary<HandlerKey, IReferenceMemoryCompositeHandler>();
            foreach (var binding in bindings)
            {
                var key = new HandlerKey(binding.NodeTypeId, binding.NodeTypeVersion);
                if (_handlers.ContainsKey(key))
                {
                    throw new ArgumentException("Memory-composite bindings must be unique by numeric type ID and version.", nameof(bindings));
                }

                _handlers.Add(key, binding.Handler);
            }
        }

        internal static ReferenceMemoryCompositeRegistry Empty { get; }
            = new ReferenceMemoryCompositeRegistry(Array.Empty<ReferenceMemoryCompositeBinding>());

        internal static ReferenceMemoryCompositeRegistry CreatePhase1BuiltIns()
        {
            return new ReferenceMemoryCompositeRegistry(new[]
            {
                new ReferenceMemoryCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"),
                    1,
                    new ReferenceMemorySequenceHandler()),
                new ReferenceMemoryCompositeBinding(
                    StableHash.Fnv1A64("aibt.core.memory-selector"),
                    1,
                    new ReferenceMemorySelectorHandler()),
            });
        }

        internal bool TryGet(ulong nodeTypeId, uint nodeTypeVersion, out IReferenceMemoryCompositeHandler handler)
            => _handlers.TryGetValue(new HandlerKey(nodeTypeId, nodeTypeVersion), out handler);

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

    internal sealed class ReferenceMemorySequenceHandler : IReferenceMemoryCompositeHandler
    {
        public NodeStatus EmptyResult => NodeStatus.Success;

        public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
        {
            if (childResult == NodeStatus.Failure)
            {
                return ReferenceCompositeDecision.Terminal(NodeStatus.Failure, childCursor);
            }

            if (childResult != NodeStatus.Success)
            {
                throw new ArgumentOutOfRangeException(nameof(childResult));
            }

            var next = checked(childCursor + 1);
            return next == childCount
                ? ReferenceCompositeDecision.Terminal(NodeStatus.Success, next)
                : ReferenceCompositeDecision.Continue(next);
        }
    }

    internal sealed class ReferenceMemorySelectorHandler : IReferenceMemoryCompositeHandler
    {
        public NodeStatus EmptyResult => NodeStatus.Failure;

        public ReferenceCompositeDecision Accept(NodeStatus childResult, uint childCursor, uint childCount)
        {
            if (childResult == NodeStatus.Success)
            {
                return ReferenceCompositeDecision.Terminal(NodeStatus.Success, childCursor);
            }

            if (childResult != NodeStatus.Failure)
            {
                throw new ArgumentOutOfRangeException(nameof(childResult));
            }

            var next = checked(childCursor + 1);
            return next == childCount
                ? ReferenceCompositeDecision.Terminal(NodeStatus.Failure, next)
                : ReferenceCompositeDecision.Continue(next);
        }
    }
}
