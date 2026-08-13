using System;
using System.Collections.Generic;

namespace AIBT
{
    internal interface IReferenceLeafHandler
    {
        void Enter(ref ReferenceNodeContext context);

        NodeStatus Tick(ref ReferenceNodeContext context);

        void Abort(ref ReferenceNodeContext context, NodeAbortReason reason);

        void Exit(ref ReferenceNodeContext context, NodeExitReason reason);
    }

    internal interface IReferenceNodeServices
    {
        bool TryStart(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            ReferenceAsyncCommandContract contract,
            ReadOnlySpan<byte> payload,
            out OperationId operationId);

        bool TryConsume(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            OperationId operationId,
            ReferenceCompletionExpectation expectation,
            out ReferenceCompletionView completion);

        bool TryCancel(
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            OperationId operationId,
            ReferenceAsyncCommandContract contract,
            ReadOnlySpan<byte> payload);

    }

    internal interface IReferenceBlackboardServices
    {
        bool TryReadBlackboard(
            RuntimeNodeIndex nodeIndex,
            uint declaredReadOrdinal,
            out BlackboardValue value);

        bool TryWriteBlackboard(
            RuntimeNodeIndex nodeIndex,
            uint declaredWriteOrdinal,
            BlackboardValue value);

        bool TryReadRegisteredBlackboard(
            RuntimeNodeIndex nodeIndex,
            uint declaredReadOrdinal,
            ulong typeId,
            uint version,
            out byte[] value);

        bool TryWriteRegisteredBlackboard(
            RuntimeNodeIndex nodeIndex,
            uint declaredWriteOrdinal,
            ulong typeId,
            uint version,
            ReadOnlySpan<byte> value);
    }

    internal ref struct ReferenceNodeContext
    {
        private readonly byte[] _config;
        private readonly byte[] _memory;
        private readonly int _configOffset;
        private readonly int _configLength;
        private readonly int _memoryOffset;
        private readonly int _memoryLength;
        private readonly IReferenceNodeServices _services;
        private readonly IReferenceBlackboardServices _blackboardServices;

        internal ReferenceNodeContext(
            byte[] config,
            int configOffset,
            int configLength,
            byte[] memory,
            int memoryOffset,
            int memoryLength,
            ReferenceUpdateContext update,
            TreeInstanceId treeInstanceId,
            RuntimeNodeIndex nodeIndex,
            uint activationGeneration,
            IReferenceNodeServices services = null,
            IReferenceBlackboardServices blackboardServices = null)
        {
            _config = config;
            _configOffset = configOffset;
            _configLength = configLength;
            _memory = memory;
            _memoryOffset = memoryOffset;
            _memoryLength = memoryLength;
            Update = update;
            TreeInstanceId = treeInstanceId;
            NodeIndex = nodeIndex;
            ActivationGeneration = activationGeneration;
            _services = services;
            _blackboardServices = blackboardServices;
        }

        internal ReferenceUpdateContext Update { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal RuntimeNodeIndex NodeIndex { get; }
        internal uint ActivationGeneration { get; }
        internal ReadOnlySpan<byte> Configuration => new ReadOnlySpan<byte>(_config, _configOffset, _configLength);
        internal Span<byte> Memory => new Span<byte>(_memory, _memoryOffset, _memoryLength);

        internal bool TryStartOperation(
            ReferenceAsyncCommandContract contract,
            ReadOnlySpan<byte> payload,
            out OperationId operationId)
        {
            operationId = default;
            return _services != null
                && _services.TryStart(NodeIndex, ActivationGeneration, contract, payload, out operationId);
        }

        internal bool TryConsumeCompletion(
            OperationId operationId,
            ReferenceCompletionExpectation expectation,
            out ReferenceCompletionView completion)
        {
            completion = default;
            return _services != null
                && _services.TryConsume(NodeIndex, ActivationGeneration, operationId, expectation, out completion);
        }

        internal bool TryCancelOperation(
            OperationId operationId,
            ReferenceAsyncCommandContract contract,
            ReadOnlySpan<byte> payload)
        {
            return _services != null
                && _services.TryCancel(NodeIndex, ActivationGeneration, operationId, contract, payload);
        }

        internal bool TryReadBlackboard(uint declaredReadOrdinal, out BlackboardValue value)
        {
            value = default;
            return _blackboardServices != null
                && _blackboardServices.TryReadBlackboard(NodeIndex, declaredReadOrdinal, out value);
        }

        internal bool TryWriteBlackboard(uint declaredWriteOrdinal, BlackboardValue value)
            => _blackboardServices != null
                && _blackboardServices.TryWriteBlackboard(NodeIndex, declaredWriteOrdinal, value);

        internal bool TryReadRegisteredBlackboard(
            uint declaredReadOrdinal,
            ulong typeId,
            uint version,
            out byte[] value)
        {
            value = null;
            return _blackboardServices != null
                && _blackboardServices.TryReadRegisteredBlackboard(NodeIndex, declaredReadOrdinal, typeId, version, out value);
        }

        internal bool TryWriteRegisteredBlackboard(
            uint declaredWriteOrdinal,
            ulong typeId,
            uint version,
            ReadOnlySpan<byte> value)
            => _blackboardServices != null
                && _blackboardServices.TryWriteRegisteredBlackboard(NodeIndex, declaredWriteOrdinal, typeId, version, value);
    }

    internal readonly struct ReferenceLeafBinding
    {
        internal ReferenceLeafBinding(ulong nodeTypeId, uint nodeTypeVersion, IReferenceLeafHandler handler)
        {
            if (nodeTypeId == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            if (nodeTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeVersion));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
        }

        internal ulong NodeTypeId { get; }
        internal uint NodeTypeVersion { get; }
        internal IReferenceLeafHandler Handler { get; }
    }

    internal sealed class ReferenceLeafRegistry
    {
        private readonly Dictionary<HandlerKey, IReferenceLeafHandler> _handlers;

        internal ReferenceLeafRegistry(IEnumerable<ReferenceLeafBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _handlers = new Dictionary<HandlerKey, IReferenceLeafHandler>();
            foreach (var binding in bindings)
            {
                var key = new HandlerKey(binding.NodeTypeId, binding.NodeTypeVersion);
                if (_handlers.ContainsKey(key))
                {
                    throw new ArgumentException("Reference leaf bindings must be unique by numeric type ID and version.", nameof(bindings));
                }

                _handlers.Add(key, binding.Handler);
            }
        }

        internal bool TryGet(ulong nodeTypeId, uint nodeTypeVersion, out IReferenceLeafHandler handler)
            => _handlers.TryGetValue(new HandlerKey(nodeTypeId, nodeTypeVersion), out handler);

        internal static ReferenceLeafRegistry CreatePhase1Fixtures()
        {
            return new ReferenceLeafRegistry(new[]
            {
                Fixture("aibt.test.success", NodeStatus.Success),
                Fixture("aibt.test.failure", NodeStatus.Failure),
                Fixture("aibt.test.running", NodeStatus.Running),
                new ReferenceLeafBinding(
                    StableHash.Fnv1A64(ReferenceAsyncActionHandler.TypeId),
                    1,
                    new ReferenceAsyncActionHandler(new ReferenceAsyncCommandContract(
                        new CommandType(StableHash.Fnv1A64("aibt.test.command.async-start"), 1),
                        new CommandType(StableHash.Fnv1A64("aibt.test.command.async-cancel"), 1)))),
            });
        }

        private static ReferenceLeafBinding Fixture(string canonicalTypeId, NodeStatus status)
        {
            return new ReferenceLeafBinding(
                StableHash.Fnv1A64(canonicalTypeId),
                1,
                new ConstantReferenceLeafHandler(status));
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

    internal sealed class ConstantReferenceLeafHandler : IReferenceLeafHandler
    {
        private readonly NodeStatus _status;

        internal ConstantReferenceLeafHandler(NodeStatus status)
        {
            if (!Enum.IsDefined(typeof(NodeStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            _status = status;
        }

        public void Enter(ref ReferenceNodeContext context)
        {
        }

        public NodeStatus Tick(ref ReferenceNodeContext context) => _status;

        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason)
        {
        }

        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason)
        {
        }
    }
}
