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
    /// P10-011: one binding of a generated leaf as the action routines see it (container only), by
    /// local binding ordinal: an async-operation binding (its action channel, start and cancel value)
    /// or a completion binding (its completion channel and value); other kinds are <see cref="None"/>.
    /// <see cref="Target"/> is the resolved target ordinal the dispatch uses for the binding.
    /// </summary>
    internal struct SimulationActionBindingV1
    {
        internal const byte None = 0;
        internal const byte AsyncOperation = 1;
        internal const byte Completion = 2;

        internal byte Kind;
        internal uint Target;
        internal int Channel;
        internal ulong PrimaryTypeId;
        internal uint PrimaryTypeVersion;
        internal uint PrimarySize;
        internal ulong SecondaryTypeId;
        internal uint SecondaryTypeVersion;
        internal uint SecondarySize;
    }

    /// <summary>
    /// P10-011: one live external action in an agent's ledger: started by a node, not yet ended. Its
    /// payloads (start, fault-cancel, completion) sit at the record's index × stride in the agent's
    /// action payload buffer. <see cref="CompletionTypeId"/> 0 is a completion without a value (zeros
    /// for every completion binding of the node).
    /// </summary>
    internal struct SimulationActionRecordV1
    {
        internal OperationId Operation;
        internal uint AsyncTarget;
        internal int Channel;
        internal ulong StartTypeId;
        internal uint StartTypeVersion;
        internal uint StartSize;
        internal ulong CancelTypeId;
        internal uint CancelTypeVersion;
        internal uint CancelSize;
        internal byte Completed;
        internal BurstCompletionOutcome Outcome;
        internal ulong CompletionTypeId;
        internal uint CompletionTypeVersion;
        internal uint CompletionSize;
    }

    /// <summary>P10-011: one action event of the current step in an agent's log; its payload is in the agent's event payload log.</summary>
    internal struct SimulationActionEventRecordV1
    {
        internal ActionEventKind Kind;
        internal ActionCancelReason Reason;
        internal byte FaultCancel;
        internal int Channel;
        internal OperationId Operation;
        internal uint PayloadOffset;
        internal uint PayloadSize;
    }

    /// <summary>
    /// P10-010: one agent's container state shared by the in-place path and the agent-frame job: the
    /// group's input channels (set before each run), the agent's row, the per-channel command quota left
    /// in this think and the agent's command log of the current step.
    /// P10-011: the agent's action ledger (live actions, fixed capacity, payloads at a fixed stride), the
    /// starts left in this think, its action event log of the current step and, during a teardown, the
    /// reason teardown cancellations carry (<see cref="TeardownReason"/> is the reason + 1, 0 when none).
    /// </summary>
    internal struct SimulationAgentNativeV1
    {
        internal UnsafeList<SimulationInputChannelV1> Inputs;
        internal UnsafeList<SimulationLeafV1> Leaves;
        internal int Row;
        internal UnsafeList<int> CommandFree;
        internal UnsafeList<SimulationCommandRecordV1> Commands;
        internal UnsafeList<byte> Payload;

        internal UnsafeList<SimulationActionRecordV1> Actions;
        internal UnsafeList<byte> ActionPayload;
        internal int ActionCapacity;
        internal int ActionStride;
        internal int ActionCancelOffset;
        internal int ActionCompletionOffset;
        internal int StartsLeft;
        internal byte TeardownReason;
        internal UnsafeList<SimulationActionEventRecordV1> Events;
        internal UnsafeList<byte> EventPayload;
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
                // P10-011: Start and Cancel commands were captured by TryCaptureActions.
                if (command.Kind == NativeBurstDispatchCommandKindV2.Start || command.Kind == NativeBurstDispatchCommandKindV2.Cancel) continue;
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
                    if (published[(int)other].Kind != NativeBurstDispatchCommandKindV2.Effect) continue;
                    var target = published[(int)other].TargetOrdinal;
                    if (target < (uint)commandChannels.Length && commandChannels[(int)target] == channel) needed++;
                }
                if (agent.CommandFree[channel] < needed) return BurstContextResult.CapacityExceeded;
            }
            for (uint index = 0; index < count; index++)
            {
                var command = published[(int)index];
                if (command.Kind != NativeBurstDispatchCommandKindV2.Effect) continue;
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

    /// <summary>
    /// P10-011: the container's external-action work around one generated-leaf dispatch, compiled into
    /// the agent-frame job (Burst) and called by the in-place path, so both produce the same ledger and
    /// events. The agent's ledger is the source of truth; the dispatch slot's operation table and
    /// completion view are rebuilt from it before every dispatch (<see cref="TryPrepare"/>) and the
    /// published Start and Cancel commands and consumed completions are folded back into it after a
    /// successful one (<see cref="TryCapture"/>).
    /// </summary>
    internal static class SimulationActionRoutinesV1
    {
        /// <summary>
        /// Before a dispatch of node <paramref name="nodeIndex"/> at activation <paramref name="activation"/>:
        /// writes the node's live actions of this activation into <paramref name="operations"/> (all
        /// Active) and the transaction's operation count, and a completion for each of them the game
        /// completed into <paramref name="completionStore"/> (one per completion binding of the node that
        /// accepts the completion's value), returning their count in <paramref name="completionCount"/>.
        /// A node without action bindings is left untouched.
        /// </summary>
        internal static BurstContextResult TryPrepare(
            ref SimulationAgentNativeV1 agent,
            NativeArray<SimulationActionBindingV1> bindings,
            uint nodeIndex,
            TreeInstanceId treeInstanceId,
            uint activation,
            NativeArray<NativeBurstDispatchOperationV2> operations,
            NativeArray<NativeBurstDispatchTransactionControlV2> transaction,
            NativeArray<NativeBurstDispatchCompletionV2> completionStore,
            NativeArray<byte> completionPayload,
            out int completionCount)
        {
            completionCount = 0;
            if (!bindings.IsCreated || bindings.Length == 0) return BurstContextResult.Success;
            var operationCount = 0;
            var payloadUsed = 0u;
            for (var index = 0; index < agent.Actions.Length; index++)
            {
                var record = agent.Actions[index];
                if (record.Operation.TreeInstanceId != treeInstanceId || record.Operation.NodeIndex.Value != nodeIndex
                    || record.Operation.ActivationGeneration != activation)
                    continue;
                if (operationCount >= operations.Length) return BurstContextResult.CapacityExceeded;
                operations[operationCount++] = new NativeBurstDispatchOperationV2(
                    record.Operation, record.AsyncTarget, record.StartTypeId, record.StartTypeVersion,
                    record.CancelTypeId, record.CancelTypeVersion, 0u, record.CancelSize);
                if (record.Completed == 0) continue;
                var recordBase = index * agent.ActionStride + agent.ActionCompletionOffset;
                for (var local = 0; local < bindings.Length; local++)
                {
                    var binding = bindings[local];
                    if (binding.Kind != SimulationActionBindingV1.Completion) continue;
                    var typeless = record.CompletionTypeId == 0;
                    if (!typeless && (binding.PrimaryTypeId != record.CompletionTypeId
                        || binding.PrimaryTypeVersion != record.CompletionTypeVersion || binding.PrimarySize != record.CompletionSize))
                        continue;
                    if (completionCount >= completionStore.Length
                        || (ulong)payloadUsed + binding.PrimarySize > (uint)completionPayload.Length)
                        return BurstContextResult.CapacityExceeded;
                    for (uint octet = 0; octet < binding.PrimarySize; octet++)
                        completionPayload[(int)(payloadUsed + octet)] = typeless ? (byte)0 : agent.ActionPayload[recordBase + (int)octet];
                    completionStore[completionCount++] = new NativeBurstDispatchCompletionV2(
                        binding.Target, record.Operation, record.Outcome, payloadUsed);
                    payloadUsed += binding.PrimarySize;
                }
            }
            for (var index = operationCount; index < operations.Length; index++) operations[index] = default;
            var control = transaction[0];
            control.OperationCount = (uint)operationCount;
            NativeBurstDispatchTransactionLedgerV2.Seal(ref control);
            transaction[0] = control;
            return BurstContextResult.Success;
        }

        /// <summary>
        /// After a successful dispatch: a published Start becomes a ledger record and a Started event, a
        /// published Cancel a Cancelled event (reason from <paramref name="abortReason"/>, the node's cancel
        /// value) that ends its record, a consumed completion ends its record without an event, and an Exit
        /// ends the node's remaining records with a Cancelled event (reason NodeEnded, or the teardown
        /// reason during a teardown; the start's fault-cancel value). Checks capacity first and changes
        /// nothing when the starts would exceed the agent's action capacity or starts left in this think
        /// (<see cref="BurstContextResult.CapacityExceeded"/>), or a command is malformed.
        /// </summary>
        internal static BurstContextResult TryCapture(
            ref SimulationAgentNativeV1 agent,
            NativeArray<SimulationActionBindingV1> bindings,
            NativeArray<NativeBurstDispatchCommandV2> published,
            NativeArray<byte> publishedPayload,
            in AgentFrameSlotStateV1 state,
            NativeArray<NativeBurstDispatchCompletionV2> completions,
            int completionCount,
            uint nodeIndex,
            BurstCallbackPhase phase,
            BurstNodeAbortReason abortReason)
        {
            if (!bindings.IsCreated || bindings.Length == 0) return BurstContextResult.Success;
            var count = state.PublishedCommandCount;
            if (count > (uint)published.Length) return BurstContextResult.TypeMismatch;
            var starts = 0;
            var cancels = 0;
            for (uint index = 0; index < count; index++)
            {
                var command = published[(int)index];
                if (command.Kind != NativeBurstDispatchCommandKindV2.Start && command.Kind != NativeBurstDispatchCommandKindV2.Cancel) continue;
                if (!TryFindBinding(bindings, command.TargetOrdinal, SimulationActionBindingV1.AsyncOperation, out var binding))
                    return BurstContextResult.TypeMismatch;
                var size = command.Kind == NativeBurstDispatchCommandKindV2.Start ? binding.PrimarySize + binding.SecondarySize : binding.SecondarySize;
                if (command.PayloadSize != (command.Kind == NativeBurstDispatchCommandKindV2.Start ? binding.PrimarySize : binding.SecondarySize)
                    || (ulong)command.PayloadOffset + size > state.PublishedPayloadByteCount
                    || (ulong)command.PayloadOffset + size > (uint)publishedPayload.Length)
                    return BurstContextResult.TypeMismatch;
                if (command.Kind == NativeBurstDispatchCommandKindV2.Start) starts++;
                else
                {
                    if (Find(ref agent, command.OperationId) < 0) return BurstContextResult.InvalidHandle;
                    cancels++;
                }
            }
            if (starts > agent.StartsLeft || agent.Actions.Length + starts > agent.ActionCapacity) return BurstContextResult.CapacityExceeded;
            var exiting = 0;
            if (phase == BurstCallbackPhase.Exit)
            {
                // Exit publishes no Start or Cancel (TryStart is Enter/Tick, TryCancel Abort only).
                for (var index = 0; index < agent.Actions.Length; index++)
                    if (agent.Actions[index].Operation.NodeIndex.Value == nodeIndex) exiting++;
            }
            if (!HasEventRoom(ref agent, starts + cancels + exiting)) return BurstContextResult.CapacityExceeded;

            for (uint index = 0; index < count; index++)
            {
                var command = published[(int)index];
                if (command.Kind == NativeBurstDispatchCommandKindV2.Start)
                {
                    TryFindBinding(bindings, command.TargetOrdinal, SimulationActionBindingV1.AsyncOperation, out var binding);
                    var slot = agent.Actions.Length;
                    agent.Actions.Add(new SimulationActionRecordV1
                    {
                        Operation = command.OperationId,
                        AsyncTarget = command.TargetOrdinal,
                        Channel = binding.Channel,
                        StartTypeId = binding.PrimaryTypeId,
                        StartTypeVersion = binding.PrimaryTypeVersion,
                        StartSize = binding.PrimarySize,
                        CancelTypeId = binding.SecondaryTypeId,
                        CancelTypeVersion = binding.SecondaryTypeVersion,
                        CancelSize = binding.SecondarySize,
                    });
                    var recordBase = slot * agent.ActionStride;
                    for (var octet = 0; octet < agent.ActionStride; octet++) agent.ActionPayload[recordBase + octet] = 0;
                    for (uint octet = 0; octet < binding.PrimarySize; octet++)
                        agent.ActionPayload[recordBase + (int)octet] = publishedPayload[(int)(command.PayloadOffset + octet)];
                    for (uint octet = 0; octet < binding.SecondarySize; octet++)
                        agent.ActionPayload[recordBase + agent.ActionCancelOffset + (int)octet]
                            = publishedPayload[(int)(command.PayloadOffset + binding.PrimarySize + octet)];
                    agent.StartsLeft--;
                    AddEvent(ref agent, ActionEventKind.Started, ActionCancelReason.Preempted, 0, binding.Channel, command.OperationId,
                        publishedPayload, (int)command.PayloadOffset, binding.PrimarySize);
                }
                else if (command.Kind == NativeBurstDispatchCommandKindV2.Cancel)
                {
                    var record = Find(ref agent, command.OperationId);
                    var reason = CancelReason(ref agent, abortReason);
                    AddEvent(ref agent, ActionEventKind.Cancelled, reason, 0, agent.Actions[record].Channel, command.OperationId,
                        publishedPayload, (int)command.PayloadOffset, command.PayloadSize);
                    Remove(ref agent, record);
                }
            }
            for (var index = 0; index < completionCount && index < completions.Length; index++)
            {
                var completion = completions[index];
                if (completion.State != NativeBurstDispatchCompletionStateV2.Consumed) continue;
                var record = Find(ref agent, completion.OperationId);
                if (record >= 0) Remove(ref agent, record);
            }
            if (phase == BurstCallbackPhase.Exit)
            {
                var reason = agent.TeardownReason != 0 ? (ActionCancelReason)(agent.TeardownReason - 1) : ActionCancelReason.NodeEnded;
                for (var index = 0; index < agent.Actions.Length;)
                {
                    if (agent.Actions[index].Operation.NodeIndex.Value != nodeIndex)
                    {
                        index++;
                        continue;
                    }
                    FaultCancel(ref agent, index, reason);
                    Remove(ref agent, index);
                }
            }
            return BurstContextResult.Success;
        }

        /// <summary>
        /// Ends every live action of the agent with a Cancelled event carrying <paramref name="reason"/> and
        /// the start's fault-cancel value (an agent fault, or what a teardown left). False only when the
        /// event log has no room, which the capacity rule excludes; the ledger is emptied either way.
        /// </summary>
        internal static bool Flush(ref SimulationAgentNativeV1 agent, ActionCancelReason reason)
        {
            if (!agent.Actions.IsCreated) return true;
            var room = HasEventRoom(ref agent, agent.Actions.Length);
            for (var index = 0; room && index < agent.Actions.Length; index++) FaultCancel(ref agent, index, reason);
            agent.Actions.Clear();
            for (var octet = 0; octet < agent.ActionPayload.Length; octet++) agent.ActionPayload[octet] = 0;
            return room;
        }

        /// <summary>The ledger as a fresh agent has it (no live action, payloads zero, no starts left, no teardown). The step's event log is kept.</summary>
        internal static void Reset(ref SimulationAgentNativeV1 agent)
        {
            if (!agent.Actions.IsCreated) return;
            agent.Actions.Clear();
            for (var octet = 0; octet < agent.ActionPayload.Length; octet++) agent.ActionPayload[octet] = 0;
            agent.StartsLeft = 0;
            agent.TeardownReason = 0;
        }

        internal static int Find(ref SimulationAgentNativeV1 agent, OperationId operation)
        {
            for (var index = 0; index < agent.Actions.Length; index++)
                if (agent.Actions[index].Operation == operation) return index;
            return -1;
        }

        private static ActionCancelReason CancelReason(ref SimulationAgentNativeV1 agent, BurstNodeAbortReason abortReason)
        {
            if (abortReason == BurstNodeAbortReason.Timeout) return ActionCancelReason.Timeout;
            if (abortReason == BurstNodeAbortReason.TreeStopped && agent.TeardownReason != 0)
                return (ActionCancelReason)(agent.TeardownReason - 1);
            return ActionCancelReason.Preempted;
        }

        private static bool TryFindBinding(
            NativeArray<SimulationActionBindingV1> bindings, uint target, byte kind, out SimulationActionBindingV1 binding)
        {
            for (var index = 0; index < bindings.Length; index++)
            {
                if (bindings[index].Kind != kind || bindings[index].Target != target) continue;
                binding = bindings[index];
                return true;
            }
            binding = default;
            return false;
        }

        private static bool HasEventRoom(ref SimulationAgentNativeV1 agent, int events)
        {
            if (events == 0) return true;
            var largest = agent.ActionCancelOffset > agent.ActionCompletionOffset - agent.ActionCancelOffset
                ? agent.ActionCancelOffset
                : agent.ActionCompletionOffset - agent.ActionCancelOffset;
            return agent.Events.Length + events <= agent.Events.Capacity
                && agent.EventPayload.Length + (long)events * largest <= agent.EventPayload.Capacity;
        }

        private static void FaultCancel(ref SimulationAgentNativeV1 agent, int index, ActionCancelReason reason)
        {
            var record = agent.Actions[index];
            var start = index * agent.ActionStride + agent.ActionCancelOffset;
            var offset = (uint)agent.EventPayload.Length;
            for (uint octet = 0; octet < record.CancelSize; octet++) agent.EventPayload.Add(agent.ActionPayload[start + (int)octet]);
            agent.Events.Add(new SimulationActionEventRecordV1
            {
                Kind = ActionEventKind.Cancelled, Reason = reason, FaultCancel = 1, Channel = record.Channel,
                Operation = record.Operation, PayloadOffset = offset, PayloadSize = record.CancelSize,
            });
        }

        private static void AddEvent(
            ref SimulationAgentNativeV1 agent, ActionEventKind kind, ActionCancelReason reason, byte faultCancel, int channel,
            OperationId operation, NativeArray<byte> payload, int payloadOffset, uint payloadSize)
        {
            var offset = (uint)agent.EventPayload.Length;
            for (uint octet = 0; octet < payloadSize; octet++) agent.EventPayload.Add(payload[payloadOffset + (int)octet]);
            agent.Events.Add(new SimulationActionEventRecordV1
            {
                Kind = kind, Reason = reason, FaultCancel = faultCancel, Channel = channel,
                Operation = operation, PayloadOffset = offset, PayloadSize = payloadSize,
            });
        }

        /// <summary>Removes record <paramref name="index"/>, keeping start order: later records and their payloads move down one.</summary>
        private static void Remove(ref SimulationAgentNativeV1 agent, int index)
        {
            var last = agent.Actions.Length - 1;
            for (var record = index; record < last; record++)
            {
                agent.Actions[record] = agent.Actions[record + 1];
                var to = record * agent.ActionStride;
                var from = to + agent.ActionStride;
                for (var octet = 0; octet < agent.ActionStride; octet++) agent.ActionPayload[to + octet] = agent.ActionPayload[from + octet];
            }
            var tail = last * agent.ActionStride;
            for (var octet = 0; octet < agent.ActionStride; octet++) agent.ActionPayload[tail + octet] = 0;
            agent.Actions.Length = last;
        }
    }
}
