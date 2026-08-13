using System;
using System.Collections.Generic;

namespace AIBT
{
    internal enum ReferenceDecoratorKind : byte
    {
        Inverter,
        Succeeder,
        Failer,
        Repeater,
        Timeout,
        Cooldown,
    }

    internal enum ReferenceCooldownStartPolicy : byte
    {
        OnEnter,
        OnSuccessfulExit,
    }

    internal readonly struct ReferenceDecoratorBinding
    {
        internal ReferenceDecoratorBinding(ulong nodeTypeId, uint nodeTypeVersion, ReferenceDecoratorKind kind)
        {
            if (nodeTypeId == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeId));
            if (nodeTypeVersion == 0) throw new ArgumentOutOfRangeException(nameof(nodeTypeVersion));
            if (!Enum.IsDefined(typeof(ReferenceDecoratorKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            NodeTypeId = nodeTypeId;
            NodeTypeVersion = nodeTypeVersion;
            Kind = kind;
        }

        internal ulong NodeTypeId { get; }
        internal uint NodeTypeVersion { get; }
        internal ReferenceDecoratorKind Kind { get; }
    }

    internal sealed class ReferenceDecoratorRegistry
    {
        private readonly Dictionary<HandlerKey, ReferenceDecoratorKind> _handlers;

        internal ReferenceDecoratorRegistry(IEnumerable<ReferenceDecoratorBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _handlers = new Dictionary<HandlerKey, ReferenceDecoratorKind>();
            foreach (var binding in bindings)
            {
                var key = new HandlerKey(binding.NodeTypeId, binding.NodeTypeVersion);
                if (_handlers.ContainsKey(key)) throw new ArgumentException("Decorator bindings must be unique.", nameof(bindings));
                _handlers.Add(key, binding.Kind);
            }
        }

        internal static ReferenceDecoratorRegistry Empty { get; }
            = new ReferenceDecoratorRegistry(Array.Empty<ReferenceDecoratorBinding>());

        internal static ReferenceDecoratorRegistry CreatePhase1BuiltIns()
        {
            return new ReferenceDecoratorRegistry(new[]
            {
                Binding("aibt.core.inverter", ReferenceDecoratorKind.Inverter),
                Binding("aibt.core.succeeder", ReferenceDecoratorKind.Succeeder),
                Binding("aibt.core.failer", ReferenceDecoratorKind.Failer),
                Binding("aibt.core.repeater", ReferenceDecoratorKind.Repeater),
                Binding("aibt.core.timeout", ReferenceDecoratorKind.Timeout),
                Binding("aibt.core.cooldown", ReferenceDecoratorKind.Cooldown),
            });
        }

        internal bool TryGet(ulong typeId, uint version, out ReferenceDecoratorKind kind)
            => _handlers.TryGetValue(new HandlerKey(typeId, version), out kind);

        private static ReferenceDecoratorBinding Binding(string typeId, ReferenceDecoratorKind kind)
            => new ReferenceDecoratorBinding(StableHash.Fnv1A64(typeId), 1, kind);

        private readonly struct HandlerKey : IEquatable<HandlerKey>
        {
            internal HandlerKey(ulong typeId, uint version) { TypeId = typeId; Version = version; }
            private ulong TypeId { get; }
            private uint Version { get; }
            public bool Equals(HandlerKey other) => TypeId == other.TypeId && Version == other.Version;
            public override bool Equals(object obj) => obj is HandlerKey other && Equals(other);
            public override int GetHashCode() { unchecked { return (TypeId.GetHashCode() * 397) ^ (int)Version; } }
        }
    }

    internal readonly struct ReferenceRepeaterConfiguration
    {
        internal ReferenceRepeaterConfiguration(uint count, bool stopOnFailure)
        {
            if (count == 0) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
            StopOnFailure = stopOnFailure;
        }
        internal uint Count { get; }
        internal bool StopOnFailure { get; }
    }

    internal readonly struct ReferenceTimeoutConfiguration
    {
        internal ReferenceTimeoutConfiguration(long durationMicroseconds, NodeStatus terminalResult)
        {
            if (durationMicroseconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationMicroseconds));
            if (terminalResult != NodeStatus.Success && terminalResult != NodeStatus.Failure) throw new ArgumentOutOfRangeException(nameof(terminalResult));
            DurationMicroseconds = durationMicroseconds;
            TerminalResult = terminalResult;
        }
        internal long DurationMicroseconds { get; }
        internal NodeStatus TerminalResult { get; }
    }

    internal readonly struct ReferenceCooldownConfiguration
    {
        internal ReferenceCooldownConfiguration(
            long durationMicroseconds,
            NodeStatus blockedResult,
            ReferenceCooldownStartPolicy startPolicy)
        {
            if (durationMicroseconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationMicroseconds));
            if (blockedResult != NodeStatus.Success && blockedResult != NodeStatus.Failure) throw new ArgumentOutOfRangeException(nameof(blockedResult));
            if (!Enum.IsDefined(typeof(ReferenceCooldownStartPolicy), startPolicy)) throw new ArgumentOutOfRangeException(nameof(startPolicy));
            DurationMicroseconds = durationMicroseconds;
            BlockedResult = blockedResult;
            StartPolicy = startPolicy;
        }
        internal long DurationMicroseconds { get; }
        internal NodeStatus BlockedResult { get; }
        internal ReferenceCooldownStartPolicy StartPolicy { get; }
    }

    internal static class ReferenceDecoratorConfigurationDecoder
    {
        internal static ReferenceRepeaterConfiguration DecodeRepeater(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 8 || bytes[4] > 1 || bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0)
                throw new ArgumentException("Invalid repeater configuration.", nameof(bytes));
            return new ReferenceRepeaterConfiguration(ReadUInt32(bytes, 0), bytes[4] != 0);
        }

        internal static ReferenceTimeoutConfiguration DecodeTimeout(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 16 || bytes[8] > 1 || HasNonZero(bytes, 9, 7))
                throw new ArgumentException("Invalid timeout configuration.", nameof(bytes));
            var duration = ReadDuration(bytes);
            return new ReferenceTimeoutConfiguration(duration, bytes[8] == 0 ? NodeStatus.Failure : NodeStatus.Success);
        }

        internal static ReferenceCooldownConfiguration DecodeCooldown(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 16 || bytes[8] > 1 || bytes[9] > 1 || HasNonZero(bytes, 10, 6))
                throw new ArgumentException("Invalid cooldown configuration.", nameof(bytes));
            var duration = ReadDuration(bytes);
            return new ReferenceCooldownConfiguration(
                duration,
                bytes[8] == 0 ? NodeStatus.Failure : NodeStatus.Success,
                bytes[9] == 0 ? ReferenceCooldownStartPolicy.OnEnter : ReferenceCooldownStartPolicy.OnSuccessfulExit);
        }

        private static long ReadDuration(ReadOnlySpan<byte> bytes)
        {
            var value = ReadUInt64(bytes, 0);
            if (value == 0 || value > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(bytes));
            return (long)value;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);

        private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (index * 8);
            return value;
        }

        private static bool HasNonZero(ReadOnlySpan<byte> bytes, int offset, int count)
        {
            for (var index = 0; index < count; index++) if (bytes[offset + index] != 0) return true;
            return false;
        }
    }
}
