using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT.Simulation
{
    /// <summary>
    /// P10-010: one input channel of a group as the staging routine sees it: the bound game array as
    /// bytes (row i = agent slot i), its row stride, and the range of its leaf table. Unbound channels
    /// stage zeros (a valid canonical value), so teardown of a group whose inputs were unbound never faults.
    /// </summary>
    internal struct SimulationInputChannelV1
    {
        internal NativeArray<byte> Rows;
        internal int RowStride;
        internal int RowCount;
        internal byte Bound;
        internal int LeafStart;
        internal int LeafCount;
        internal uint CanonicalSize;
    }

    /// <summary>
    /// P10-010: one scalar of a value: where it is in the game's raw <c>T</c> and where it goes in the
    /// canonical encoding the dispatch reads (fields ordered by stable field id, natural alignment,
    /// little-endian; see <c>Planning~/Evidence/P10-010/README.md</c>).
    /// </summary>
    internal struct SimulationLeafV1
    {
        internal uint RawOffset;
        internal uint CanonicalOffset;
        internal uint Size;
        internal NativeBurstDispatchFieldEncodingV2 Encoding;
    }

    /// <summary>P10-010: one command an agent emitted in the current step: channel and its canonical payload bytes in the agent's payload log.</summary>
    internal struct SimulationCommandRecordV1
    {
        internal int Channel;
        internal uint PayloadOffset;
        internal uint PayloadSize;
    }

    /// <summary>P10-010: an agent that emitted at least one command in the last step, with its generation at that step.</summary>
    internal struct SimulationEmitterV1
    {
        internal int Index;
        internal uint Generation;
    }

    /// <summary>
    /// P10-010: one agent's container state shared by the in-place path and the agent-frame job: the
    /// group's input channels (set before each run), the agent's row, the per-channel command quota left
    /// in this think and the agent's command log of the current step.
    /// </summary>
    internal struct SimulationAgentNativeV1
    {
        internal UnsafeList<SimulationInputChannelV1> Inputs;
        internal UnsafeList<SimulationLeafV1> Leaves;
        internal int Row;
        internal UnsafeList<int> CommandFree;
        internal UnsafeList<SimulationCommandRecordV1> Commands;
        internal UnsafeList<byte> Payload;
    }

    /// <summary>
    /// P10-010: how an agent's main-thread dispatch (in place) reaches its container state: the owning
    /// group's per-slot native entry, by reference.
    /// </summary>
    internal sealed class SimulationAgentBridgeV1
    {
        private readonly TreeGroup _group;
        private readonly int _index;

        internal SimulationAgentBridgeV1(TreeGroup group, int index)
        {
            _group = group;
            _index = index;
        }

        internal ref SimulationAgentNativeV1 Agent => ref _group.SimulationAgent(_index);
    }

    /// <summary>
    /// P10-010: the container's per-dispatch work, compiled into the agent-frame job (Burst) and called
    /// by the in-place path, so both produce the same bytes.
    /// </summary>
    internal static class SimulationAgentRoutinesV1
    {
        /// <summary>
        /// Writes this agent's row of input channel <paramref name="channel"/> into
        /// <paramref name="destination"/> at <paramref name="offset"/> in canonical form: padding zero,
        /// each leaf copied from its raw offset, -0.0 written as +0.0. NaN, infinities and bools above 1
        /// are copied as they are; the dispatch refuses them (the agent faults with InvalidEncoding). An
        /// unbound channel or a row outside the array stages zeros.
        /// </summary>
        internal static bool TryStage(
            ref SimulationAgentNativeV1 agent, int channel, NativeArray<byte> destination, uint offset, uint size)
        {
            if ((ulong)offset + size > (uint)destination.Length) return false;
            for (uint index = 0; index < size; index++) destination[(int)(offset + index)] = 0;
            if ((uint)channel >= (uint)agent.Inputs.Length) return false;
            var input = agent.Inputs[channel];
            if (input.CanonicalSize != size) return false;
            if (input.Bound == 0 || (uint)agent.Row >= (uint)input.RowCount) return true;
            var rowBase = agent.Row * input.RowStride;
            for (var leafIndex = 0; leafIndex < input.LeafCount; leafIndex++)
            {
                var leaf = agent.Leaves[input.LeafStart + leafIndex];
                if ((ulong)leaf.CanonicalOffset + leaf.Size > size
                    || (ulong)leaf.RawOffset + leaf.Size > (uint)input.RowStride) return false;
                var source = rowBase + (int)leaf.RawOffset;
                var target = (int)(offset + leaf.CanonicalOffset);
                for (var octet = 0; octet < (int)leaf.Size; octet++) destination[target + octet] = input.Rows[source + octet];
                if (leaf.Encoding == NativeBurstDispatchFieldEncodingV2.Float32 && leaf.Size == 4)
                {
                    if (destination[target] == 0 && destination[target + 1] == 0 && destination[target + 2] == 0 && destination[target + 3] == 0x80)
                        destination[target + 3] = 0;
                }
                else if (leaf.Encoding == NativeBurstDispatchFieldEncodingV2.Float64 && leaf.Size == 8)
                {
                    var negativeZero = destination[target + 7] == 0x80;
                    for (var octet = 0; negativeZero && octet < 7; octet++) negativeZero = destination[target + octet] == 0;
                    if (negativeZero) destination[target + 7] = 0;
                }
            }
            return true;
        }

        /// <summary>
        /// The container's command quota check before a generated leaf runs (the effect-capacity rule):
        /// every command binding of the node needs one free command on its channel in this think.
        /// </summary>
        internal static BurstContextResult TryReserve(ref SimulationAgentNativeV1 agent, NativeArray<int> commandChannels)
        {
            if (!commandChannels.IsCreated) return BurstContextResult.Success;
            for (var local = 0; local < commandChannels.Length; local++)
            {
                var channel = commandChannels[local];
                if (channel < 0) continue;
                if ((uint)channel >= (uint)agent.CommandFree.Length) return BurstContextResult.InvalidHandle;
                // Two bindings of one node may name the same channel: each needs its own command.
                var needed = 0;
                for (var other = 0; other < commandChannels.Length; other++)
                    if (commandChannels[other] == channel) needed++;
                if (agent.CommandFree[channel] < needed) return BurstContextResult.CapacityExceeded;
            }
            return BurstContextResult.Success;
        }

        /// <summary>
        /// Moves the commands a successful dispatch published into the agent's step log (channel, canonical
        /// payload), all or nothing, and clears the published record as effect capture does.
        /// </summary>
        internal static BurstContextResult TryCapture(
            ref SimulationAgentNativeV1 agent,
            NativeArray<int> commandChannels,
            NativeArray<NativeBurstDispatchCommandV2> published,
            NativeArray<byte> publishedPayload,
            ref AgentFrameSlotStateV1 state)
        {
            var count = state.PublishedCommandCount;
            if (count == 0) return BurstContextResult.Success;
            if (!commandChannels.IsCreated)
            {
                // A catalog without binding ids: its effects are discarded, as in stage G1.
                state.PublishedCommandCount = 0;
                state.PublishedPayloadByteCount = 0;
                return BurstContextResult.Success;
            }
            if (count > (uint)published.Length) return BurstContextResult.TypeMismatch;
            for (uint index = 0; index < count; index++)
            {
                var command = published[(int)index];
                if (command.Kind != NativeBurstDispatchCommandKindV2.Effect
                    || command.TargetOrdinal >= (uint)commandChannels.Length
                    || (ulong)command.PayloadOffset + command.PayloadSize > state.PublishedPayloadByteCount
                    || (ulong)command.PayloadOffset + command.PayloadSize > (uint)publishedPayload.Length)
                    return BurstContextResult.TypeMismatch;
                var channel = commandChannels[(int)command.TargetOrdinal];
                if (channel < 0) continue; // no binding id: discarded
                if ((uint)channel >= (uint)agent.CommandFree.Length) return BurstContextResult.InvalidHandle;
                var needed = 0;
                for (uint other = 0; other < count; other++)
                {
                    var target = published[(int)other].TargetOrdinal;
                    if (target < (uint)commandChannels.Length && commandChannels[(int)target] == channel) needed++;
                }
                if (agent.CommandFree[channel] < needed) return BurstContextResult.CapacityExceeded;
            }
            for (uint index = 0; index < count; index++)
            {
                var command = published[(int)index];
                var channel = commandChannels[(int)command.TargetOrdinal];
                if (channel < 0) continue;
                agent.CommandFree[channel] = agent.CommandFree[channel] - 1;
                agent.Commands.Add(new SimulationCommandRecordV1
                {
                    Channel = channel,
                    PayloadOffset = (uint)agent.Payload.Length,
                    PayloadSize = command.PayloadSize,
                });
                for (uint octet = 0; octet < command.PayloadSize; octet++)
                    agent.Payload.Add(publishedPayload[(int)(command.PayloadOffset + octet)]);
            }
            state.PublishedCommandCount = 0;
            state.PublishedPayloadByteCount = 0;
            return BurstContextResult.Success;
        }
    }
}
