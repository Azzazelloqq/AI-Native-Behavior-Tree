using Unity.Collections;

namespace AIBT
{
    internal enum NativeDecoratorKindV1 : byte { Inverter, Succeeder, Failer, Repeater, Timeout, Cooldown }
    internal enum NativeCooldownStartPolicyV1 : byte { OnEnter, OnSuccessfulExit }

    internal readonly struct NativeRepeaterConfigurationV1
    {
        internal NativeRepeaterConfigurationV1(uint count, bool stopOnFailure) { Count = count; StopOnFailure = stopOnFailure; }
        internal uint Count { get; }
        internal bool StopOnFailure { get; }
    }

    internal readonly struct NativeDeadlineConfigurationV1
    {
        internal NativeDeadlineConfigurationV1(long duration, NodeStatus result, NativeCooldownStartPolicyV1 startPolicy = default)
        { DurationMicroseconds = duration; Result = result; StartPolicy = startPolicy; }
        internal long DurationMicroseconds { get; }
        internal NodeStatus Result { get; }
        internal NativeCooldownStartPolicyV1 StartPolicy { get; }
    }

    internal static class NativeDecoratorPolicyV1
    {
        internal static bool TryGetKind(ulong typeId, out NativeDecoratorKindV1 kind)
        {
            switch (typeId)
            {
                case 0x1d94660c834c2dd3UL: kind = NativeDecoratorKindV1.Inverter; return true;
                case 0x62bf753b2ce0768bUL: kind = NativeDecoratorKindV1.Succeeder; return true;
                case 0x99e29ceca488f863UL: kind = NativeDecoratorKindV1.Failer; return true;
                case 0x433e57b7756e9374UL: kind = NativeDecoratorKindV1.Repeater; return true;
                case 0xaa30c20be5171f83UL: kind = NativeDecoratorKindV1.Timeout; return true;
                case 0x3cf19b9a0dc11e21UL: kind = NativeDecoratorKindV1.Cooldown; return true;
                default: kind = default; return false;
            }
        }

        internal static NodeStatus Transform(NativeDecoratorKindV1 kind, NodeStatus child)
        {
            if (child == NodeStatus.Running) return child;
            switch (kind)
            {
                case NativeDecoratorKindV1.Inverter: return child == NodeStatus.Success ? NodeStatus.Failure : NodeStatus.Success;
                case NativeDecoratorKindV1.Succeeder: return NodeStatus.Success;
                case NativeDecoratorKindV1.Failer: return NodeStatus.Failure;
                default: return child;
            }
        }

        internal static bool TryDecodeRepeater(NativeArray<byte>.ReadOnly bytes, uint offset, uint size, out NativeRepeaterConfigurationV1 value)
        {
            value = default;
            if (!Range(bytes, offset, size, 8)) return false;
            var start = (int)offset;
            if (bytes[start + 4] > 1 || bytes[start + 5] != 0 || bytes[start + 6] != 0 || bytes[start + 7] != 0) return false;
            var count = ReadU32(bytes, start);
            if (count == 0) return false;
            value = new NativeRepeaterConfigurationV1(count, bytes[start + 4] != 0);
            return true;
        }

        internal static bool TryDecodeTimeout(NativeArray<byte>.ReadOnly bytes, uint offset, uint size, out NativeDeadlineConfigurationV1 value)
        {
            value = default;
            if (!Range(bytes, offset, size, 16)) return false;
            var start = (int)offset;
            if (bytes[start + 8] > 1 || HasNonZero(bytes, start + 9, 7)) return false;
            var duration = ReadU64(bytes, start);
            if (duration == 0 || duration > long.MaxValue) return false;
            value = new NativeDeadlineConfigurationV1((long)duration, bytes[start + 8] == 0 ? NodeStatus.Failure : NodeStatus.Success);
            return true;
        }

        internal static bool TryDecodeCooldown(NativeArray<byte>.ReadOnly bytes, uint offset, uint size, out NativeDeadlineConfigurationV1 value)
        {
            value = default;
            if (!Range(bytes, offset, size, 16)) return false;
            var start = (int)offset;
            if (bytes[start + 8] > 1 || bytes[start + 9] > 1 || HasNonZero(bytes, start + 10, 6)) return false;
            var duration = ReadU64(bytes, start);
            if (duration == 0 || duration > long.MaxValue) return false;
            value = new NativeDeadlineConfigurationV1(
                (long)duration,
                bytes[start + 8] == 0 ? NodeStatus.Failure : NodeStatus.Success,
                (NativeCooldownStartPolicyV1)bytes[start + 9]);
            return true;
        }

        internal static bool TryDeadline(long now, long duration, out long deadline)
        {
            deadline = 0;
            if (duration <= 0 || now > long.MaxValue - duration) return false;
            deadline = now + duration;
            return true;
        }

        private static bool Range(NativeArray<byte>.ReadOnly bytes, uint offset, uint size, uint exact)
            => bytes.IsCreated && size == exact && offset <= bytes.Length && size <= bytes.Length - offset;
        private static bool HasNonZero(NativeArray<byte>.ReadOnly bytes, int offset, int count)
        { for (var index = 0; index < count; index++) if (bytes[offset + index] != 0) return true; return false; }
        private static uint ReadU32(NativeArray<byte>.ReadOnly bytes, int offset)
            => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        private static ulong ReadU64(NativeArray<byte>.ReadOnly bytes, int offset)
        { ulong value = 0; for (var index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (index * 8); return value; }
    }
}
