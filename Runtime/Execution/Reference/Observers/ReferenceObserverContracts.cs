using System;
using System.Collections.Generic;

namespace AIBT
{
    internal interface IReferenceObserverConditionEvaluator
    {
        NodeStatus Evaluate(ref ReferenceObserverConditionContext context);
    }

    internal ref struct ReferenceObserverConditionContext
    {
        private readonly byte[] _configuration;
        private readonly int _configurationOffset;
        private readonly int _configurationLength;
        private readonly ReferenceBlackboardStorage _blackboard;
        private readonly IReferenceObserverReadServices _readServices;

        internal ReferenceObserverConditionContext(
            byte[] configuration,
            int configurationOffset,
            int configurationLength,
            ReferenceUpdateContext update,
            TreeInstanceId treeInstanceId,
            RuntimeNodeIndex nodeIndex,
            ReferenceBlackboardStorage blackboard,
            IReferenceObserverReadServices readServices)
        {
            _configuration = configuration;
            _configurationOffset = configurationOffset;
            _configurationLength = configurationLength;
            _blackboard = blackboard;
            _readServices = readServices;
            Update = update;
            TreeInstanceId = treeInstanceId;
            NodeIndex = nodeIndex;
        }

        internal ReferenceUpdateContext Update { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal RuntimeNodeIndex NodeIndex { get; }
        internal ReadOnlySpan<byte> Configuration
            => new ReadOnlySpan<byte>(_configuration, _configurationOffset, _configurationLength);

        internal bool TryRead(uint declaredReadOrdinal, out BlackboardValue value)
        {
            if (_blackboard == null)
            {
                value = default;
                return false;
            }
            var result = _blackboard.TryRead(NodeIndex, declaredReadOrdinal, out value);
            _readServices.RecordRead(result);
            return result.Success;
        }

        internal bool TryReadRegistered(
            uint declaredReadOrdinal,
            ulong typeId,
            uint version,
            out byte[] value)
        {
            if (_blackboard == null)
            {
                value = null;
                return false;
            }
            var result = _blackboard.TryReadRegistered(NodeIndex, declaredReadOrdinal, typeId, version, out value);
            _readServices.RecordRead(result);
            return result.Success;
        }
    }

    internal interface IReferenceObserverReadServices
    {
        void RecordRead(BlackboardStorageResult result);
    }

    internal readonly struct ReferenceObserverConditionBinding
    {
        internal ReferenceObserverConditionBinding(
            ulong nodeTypeId,
            uint nodeTypeVersion,
            IReferenceObserverConditionEvaluator evaluator)
        {
            if (nodeTypeId == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            if (nodeTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeVersion));
            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
            Evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        }

        internal ulong NodeTypeId { get; }
        internal uint NodeTypeVersion { get; }
        internal IReferenceObserverConditionEvaluator Evaluator { get; }
    }

    internal sealed class ReferenceObserverConditionRegistry
    {
        private readonly Dictionary<HandlerKey, IReferenceObserverConditionEvaluator> _evaluators;

        internal ReferenceObserverConditionRegistry(IEnumerable<ReferenceObserverConditionBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _evaluators = new Dictionary<HandlerKey, IReferenceObserverConditionEvaluator>();
            foreach (var binding in bindings)
            {
                var key = new HandlerKey(binding.NodeTypeId, binding.NodeTypeVersion);
                if (_evaluators.ContainsKey(key))
                    throw new ArgumentException("Observer condition bindings must be unique by type ID and version.", nameof(bindings));
                _evaluators.Add(key, binding.Evaluator);
            }
        }

        internal static ReferenceObserverConditionRegistry Empty { get; }
            = new ReferenceObserverConditionRegistry(Array.Empty<ReferenceObserverConditionBinding>());

        internal bool TryGet(
            ulong nodeTypeId,
            uint nodeTypeVersion,
            out IReferenceObserverConditionEvaluator evaluator)
            => _evaluators.TryGetValue(new HandlerKey(nodeTypeId, nodeTypeVersion), out evaluator);

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

    internal sealed class ReferenceObserverRuntimeState
    {
        internal bool IsQueued { get; set; }
        internal bool HasLastResult { get; set; }
        internal NodeStatus LastResult { get; set; }
    }
}
