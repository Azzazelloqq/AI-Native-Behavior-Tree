using System;
using System.Collections.Generic;

namespace AIBT
{
    internal enum ReferenceParallelPolicy : byte
    {
        RequireAllSuccess,
        RequireAnySuccess,
        Threshold,
    }

    internal enum ReferenceParallelTieBreak : byte
    {
        FailureFirst,
        SuccessFirst,
    }

    internal readonly struct ReferenceParallelConfiguration
    {
        internal ReferenceParallelConfiguration(
            ReferenceParallelPolicy policy,
            uint successThreshold,
            uint failureThreshold,
            ReferenceParallelTieBreak tieBreak,
            uint childCount)
        {
            if (childCount == 0) throw new ArgumentOutOfRangeException(nameof(childCount));
            if (!Enum.IsDefined(typeof(ReferenceParallelPolicy), policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            if (!Enum.IsDefined(typeof(ReferenceParallelTieBreak), tieBreak)) throw new ArgumentOutOfRangeException(nameof(tieBreak));
            if (policy == ReferenceParallelPolicy.Threshold)
            {
                if (successThreshold == 0 || failureThreshold == 0
                    || successThreshold > childCount || failureThreshold > childCount
                    || (ulong)successThreshold + failureThreshold > (ulong)childCount + 1)
                    throw new ArgumentOutOfRangeException(nameof(successThreshold));
            }
            else if (successThreshold != 0 || failureThreshold != 0 || tieBreak != ReferenceParallelTieBreak.FailureFirst)
            {
                throw new ArgumentException("Non-threshold policies cannot carry thresholds.");
            }
            Policy = policy;
            SuccessThreshold = successThreshold;
            FailureThreshold = failureThreshold;
            TieBreak = tieBreak;
        }
        internal ReferenceParallelPolicy Policy { get; }
        internal uint SuccessThreshold { get; }
        internal uint FailureThreshold { get; }
        internal ReferenceParallelTieBreak TieBreak { get; }
    }

    internal readonly struct ReferenceParallelBinding
    {
        internal ReferenceParallelBinding(ulong nodeTypeId, uint nodeTypeVersion)
        {
            if (nodeTypeId == 0 || nodeTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
        }
        internal ulong NodeTypeId { get; }
        internal uint NodeTypeVersion { get; }
    }

    internal sealed class ReferenceParallelRegistry
    {
        private readonly HashSet<HandlerKey> _keys;
        internal ReferenceParallelRegistry(IEnumerable<ReferenceParallelBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _keys = new HashSet<HandlerKey>();
            foreach (var binding in bindings)
                if (!_keys.Add(new HandlerKey(binding.NodeTypeId, binding.NodeTypeVersion))) throw new ArgumentException("Parallel bindings must be unique.", nameof(bindings));
        }
        internal static ReferenceParallelRegistry Empty { get; } = new ReferenceParallelRegistry(Array.Empty<ReferenceParallelBinding>());
        internal static ReferenceParallelRegistry CreatePhase1BuiltIns() => new ReferenceParallelRegistry(new[]
        {
            new ReferenceParallelBinding(StableHash.Fnv1A64("aibt.core.parallel"), 1),
        });
        internal bool Contains(ulong id, uint version) => _keys.Contains(new HandlerKey(id, version));

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

    internal static class ReferenceParallelConfigurationDecoder
    {
        internal static ReferenceParallelConfiguration Decode(ReadOnlySpan<byte> bytes, uint childCount)
        {
            if (bytes.Length != 16 || bytes[0] > 2 || bytes[12] > 1
                || bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0
                || bytes[13] != 0 || bytes[14] != 0 || bytes[15] != 0)
                throw new ArgumentException("Invalid parallel configuration.", nameof(bytes));
            var policy = (ReferenceParallelPolicy)bytes[0];
            var success = ReadUInt32(bytes, 4);
            var failure = ReadUInt32(bytes, 8);
            if (policy != ReferenceParallelPolicy.Threshold && bytes[12] != 0)
                throw new ArgumentException("Non-threshold policy cannot carry a tie-break.", nameof(bytes));
            return new ReferenceParallelConfiguration(policy, success, failure, (ReferenceParallelTieBreak)bytes[12], childCount);
        }
        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
    }

    internal readonly struct ReferenceParallelDecision
    {
        private ReferenceParallelDecision(bool isTerminal, NodeStatus status)
        {
            IsTerminal = isTerminal;
            Status = status;
        }

        internal bool IsTerminal { get; }
        internal NodeStatus Status { get; }
        internal static ReferenceParallelDecision Waiting { get; } = new ReferenceParallelDecision(false, NodeStatus.Running);
        internal static ReferenceParallelDecision Terminal(NodeStatus status)
        {
            if (status != NodeStatus.Success && status != NodeStatus.Failure)
                throw new ArgumentOutOfRangeException(nameof(status));
            return new ReferenceParallelDecision(true, status);
        }
    }

    internal static class ReferenceParallelPolicyEvaluator
    {
        internal static ReferenceParallelDecision Evaluate(
            ReferenceParallelConfiguration configuration,
            IReadOnlyList<ReferenceParallelBranch> branches)
        {
            if (branches == null || branches.Count == 0) throw new ArgumentException("Parallel branches are required.", nameof(branches));
            uint successes = 0;
            uint failures = 0;
            for (var index = 0; index < branches.Count; index++)
            {
                switch (branches[index].State)
                {
                    case ReferenceParallelChildState.NotStarted:
                    case ReferenceParallelChildState.Running:
                        break;
                    case ReferenceParallelChildState.Success:
                        successes++;
                        break;
                    case ReferenceParallelChildState.Failure:
                        failures++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(branches));
                }
            }

            switch (configuration.Policy)
            {
                case ReferenceParallelPolicy.RequireAllSuccess:
                    if (failures != 0) return ReferenceParallelDecision.Terminal(NodeStatus.Failure);
                    return successes == branches.Count
                        ? ReferenceParallelDecision.Terminal(NodeStatus.Success)
                        : ReferenceParallelDecision.Waiting;
                case ReferenceParallelPolicy.RequireAnySuccess:
                    if (successes != 0) return ReferenceParallelDecision.Terminal(NodeStatus.Success);
                    return failures == branches.Count
                        ? ReferenceParallelDecision.Terminal(NodeStatus.Failure)
                        : ReferenceParallelDecision.Waiting;
                case ReferenceParallelPolicy.Threshold:
                    var successReached = successes >= configuration.SuccessThreshold;
                    var failureReached = failures >= configuration.FailureThreshold;
                    if (successReached && failureReached)
                    {
                        return ReferenceParallelDecision.Terminal(
                            configuration.TieBreak == ReferenceParallelTieBreak.SuccessFirst
                                ? NodeStatus.Success
                                : NodeStatus.Failure);
                    }
                    if (successReached) return ReferenceParallelDecision.Terminal(NodeStatus.Success);
                    if (failureReached) return ReferenceParallelDecision.Terminal(NodeStatus.Failure);
                    return ReferenceParallelDecision.Waiting;
                default:
                    throw new ArgumentOutOfRangeException(nameof(configuration));
            }
        }
    }
}
