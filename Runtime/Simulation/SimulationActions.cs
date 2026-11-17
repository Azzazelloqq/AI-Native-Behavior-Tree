using System;

namespace AIBT.Simulation
{
    /// <summary>P10-011: how the game ends an external action (<see cref="TreeGroup.TryComplete(ActionHandle, ActionOutcome)"/>). Experimental until G5.</summary>
    public enum ActionOutcome : byte
    {
        /// <summary>The node reads <see cref="AIBT.Burst.BurstCompletionOutcome.Succeeded"/>.</summary>
        Succeeded = 0,
        /// <summary>The node reads <see cref="AIBT.Burst.BurstCompletionOutcome.Failed"/>.</summary>
        Failed = 1,
    }

    /// <summary>P10-011: what happened to an external action in the last step. Experimental until G5.</summary>
    public enum ActionEventKind : byte
    {
        /// <summary>A node started the action: begin it in the game, then complete it with <see cref="TreeGroup.TryComplete(ActionHandle, ActionOutcome)"/>.</summary>
        Started = 0,
        /// <summary>The action ended without its completion being consumed: stop it in the game. Its handle is stale from now on.</summary>
        Cancelled = 1,
    }

    /// <summary>
    /// P10-011: why an action was cancelled. Every started action that does not end by its node consuming
    /// the game's completion gets exactly one <see cref="ActionEventKind.Cancelled"/> event, with one of these
    /// reasons. Experimental until G5.
    /// </summary>
    public enum ActionCancelReason : byte
    {
        /// <summary>The node was aborted (a reactive or observer interrupt, a parallel or explicit abort) and cancelled the action in its Abort.</summary>
        Preempted = 0,
        /// <summary>The node was aborted by a timeout decorator and cancelled the action in its Abort.</summary>
        Timeout = 1,
        /// <summary>The node ended (exited) while the action was live and did not cancel it: the container cancels it with the start's fault-cancel payload.</summary>
        NodeEnded = 2,
        /// <summary>The agent faulted while the action was live (fault-cancel payload).</summary>
        Fault = 3,
        /// <summary>The agent was removed (<see cref="TreeGroup.TryRemove"/>): its teardown ended the action.</summary>
        AgentRemoved = 4,
        /// <summary>The agent was restarted (<see cref="TreeGroup.TryRestart"/>): its teardown ended the action.</summary>
        AgentRestarted = 5,
        /// <summary>The agent's group was removed (<see cref="TreeSimulation.TryRemoveGroup"/>): its teardown ended the action.</summary>
        GroupRemoved = 6,
    }

    /// <summary>
    /// P10-011: one external action: the agent that started it, the operation id the dispatch assigned
    /// (tree instance, node, activation, sequence) and its channel (the async-operation binding id, as
    /// <see cref="CommandChannel{T}.Id"/> for a command). A handle stays valid until the action's
    /// <see cref="ActionEventKind.Cancelled"/> event or until its node consumes the completion; after that
    /// calls with it return <see cref="SimulationResult.StaleAction"/>. Experimental until G5.
    /// </summary>
    public readonly struct ActionHandle : IEquatable<ActionHandle>
    {
        internal ActionHandle(AgentHandle agent, OperationId operation, ulong channel)
        {
            Agent = agent;
            Operation = operation;
            Channel = channel;
        }

        public AgentHandle Agent { get; }
        public OperationId Operation { get; }
        /// <summary>The async-operation binding id (FNV-1a 64 of the binding's id string).</summary>
        public ulong Channel { get; }
        public bool IsNull => Agent.IsNull;

        public bool Equals(ActionHandle other) => Agent == other.Agent && Operation == other.Operation && Channel == other.Channel;
        public override bool Equals(object obj) => obj is ActionHandle other && Equals(other);
        public override int GetHashCode() => unchecked(Agent.GetHashCode() * 397 ^ Operation.GetHashCode() ^ Channel.GetHashCode());
        public static bool operator ==(ActionHandle left, ActionHandle right) => left.Equals(right);
        public static bool operator !=(ActionHandle left, ActionHandle right) => !left.Equals(right);
        public override string ToString() => "ActionHandle(" + Agent + ", " + Operation + ")";
    }

    /// <summary>
    /// P10-011: one action event of the last step (<see cref="TreeGroup.GetActionEvents"/>). Read the start
    /// payload of a <see cref="ActionEventKind.Started"/> event with
    /// <see cref="TreeGroup.TryGetStartPayload{TStart}"/> and the cancel payload of a
    /// <see cref="ActionEventKind.Cancelled"/> event with <see cref="TreeGroup.TryGetCancelPayload{TCancel}"/>,
    /// before the next step. Experimental until G5.
    /// </summary>
    public readonly struct ActionEvent
    {
        internal ActionEvent(
            ActionEventKind kind, ActionHandle action, ActionCancelReason reason, bool faultCancel,
            int channel, uint payloadOffset, uint payloadSize, uint stamp)
        {
            Kind = kind;
            Action = action;
            CancelReason = reason;
            FaultCancel = faultCancel;
            ChannelIndex = channel;
            PayloadOffset = payloadOffset;
            PayloadSize = payloadSize;
            Stamp = stamp;
        }

        public ActionEventKind Kind { get; }
        public ActionHandle Action { get; }
        /// <summary>For <see cref="ActionEventKind.Cancelled"/>: why. <see cref="ActionCancelReason.Preempted"/> (0) for a Started event.</summary>
        public ActionCancelReason CancelReason { get; }
        /// <summary>
        /// For <see cref="ActionEventKind.Cancelled"/>: true when the cancel payload is the fault-cancel value
        /// the node gave at start (the node did not cancel the action itself), false when the node wrote it in Abort.
        /// </summary>
        public bool FaultCancel { get; }

        internal int ChannelIndex { get; }
        internal uint PayloadOffset { get; }
        internal uint PayloadSize { get; }
        internal uint Stamp { get; }
    }
}
