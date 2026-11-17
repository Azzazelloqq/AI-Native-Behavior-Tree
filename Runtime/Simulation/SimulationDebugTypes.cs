using System;
using AIBT.Burst;

namespace AIBT.Simulation
{
    /// <summary>
    /// P10-012: why an agent thought in a step (<see cref="AgentTraceFact.Think"/>,
    /// <see cref="AgentDebugState.LastThinkReasons"/>). Several can hold at once. Experimental until G5.
    /// </summary>
    [Flags]
    public enum ThinkReasons : byte
    {
        None = 0,
        /// <summary>The step's tick matched the agent's think schedule.</summary>
        Scheduled = 1 << 0,
        /// <summary>The game woke it (<see cref="TreeGroup.TryWake"/>, <see cref="TreeGroup.TryWakeMany"/>).</summary>
        WokenByGame = 1 << 1,
        /// <summary>Wake on changed input: a watched input row differed from the one it read at its last think.</summary>
        InputChanged = 1 << 2,
        /// <summary>The game completed one of its actions (<see cref="TreeGroup.TryComplete(ActionHandle, ActionOutcome)"/>).</summary>
        ActionCompleted = 1 << 3,
        /// <summary>The think started a fresh instance: the agent was just added or restarted, or its root finished under <see cref="RootCompletionPolicy.Restart"/>.</summary>
        FreshInstance = 1 << 4,
    }

    /// <summary>
    /// P10-012: what one <see cref="AgentTraceRecord"/> says. <see cref="Lifecycle"/> records are written by
    /// the package's native trace recorder, exactly as a <see cref="ProductionTreeHost"/> records them
    /// (update started/completed with the root result, node entered/ticked/exited/aborted with status and
    /// reasons); every other fact is the container's own and carries a native record of the nearest existing
    /// <see cref="NativeTraceEventKindV1"/> so the Editor's trace tools can show it. Experimental until G5.
    /// </summary>
    public enum AgentTraceFact : byte
    {
        /// <summary>A native trace record of the tree's lifecycle (<see cref="AgentTraceRecord.Record"/>).</summary>
        Lifecycle = 0,
        /// <summary>The agent thinks in this step, for <see cref="AgentTraceRecord.Reasons"/>. Native kind SchedulerDecision (phase SelectWork).</summary>
        Think = 1,
        /// <summary>
        /// The row of one input channel the program reads, as this think sees it (canonical bytes; inputs do
        /// not change during a step, so every read of the think returns it). Native kind SchedulerDecision (phase CollectInput).
        /// </summary>
        Input = 2,
        /// <summary>A command the agent emitted (channel and canonical payload). Native kind CommandEmitted (phase PublishCommands).</summary>
        Command = 3,
        /// <summary>A node started an external action (<see cref="AgentTraceRecord.Operation"/>, start payload). Native kind CommandEmitted (phase ApplyIntegrations).</summary>
        ActionStarted = 4,
        /// <summary>An action was cancelled (<see cref="AgentTraceRecord.CancelReason"/>, cancel payload). Native kind CompletionDiscarded.</summary>
        ActionCancelled = 5,
        /// <summary>A node read the game's completion of its action and the action ended. Native kind CompletionConsumed.</summary>
        CompletionConsumed = 6,
        /// <summary>The agent faulted (<see cref="AgentTraceRecord.Failure"/>). Native kind DiagnosticRaised with the failure's code number.</summary>
        Fault = 7,
        /// <summary>The agent is torn down before this step's thinks (<see cref="AgentTraceRecord.CancelReason"/>: AgentRemoved, AgentRestarted or GroupRemoved). Native kind SchedulerDecision.</summary>
        Teardown = 8,
    }

    /// <summary>
    /// P10-012: one entry of a traced agent's trace (<see cref="TreeGroup.TryCopyTrace"/>). The order inside
    /// one step is fixed: a teardown block (Teardown, its lifecycle, commands, action events), then a think
    /// block (Think, Input per channel, lifecycle, commands, action events, consumed completions, fault).
    /// Values are kept up to 16 bytes (<see cref="ValueSize"/> is the full size). Experimental until G5.
    /// </summary>
    public readonly struct AgentTraceRecord
    {
        internal AgentTraceRecord(
            uint tick, AgentTraceFact fact, in NativeTraceRecordV1 record, ThinkReasons reasons, ulong channel,
            OperationId operation, ActionCancelReason cancelReason, NativeRuntimeFailureV1 failure,
            uint valueSize, ulong valueLow, ulong valueHigh)
        {
            Tick = tick;
            Fact = fact;
            Record = record;
            Reasons = reasons;
            Channel = channel;
            Operation = operation;
            CancelReason = cancelReason;
            Failure = failure;
            ValueSize = valueSize;
            ValueLow = valueLow;
            ValueHigh = valueHigh;
        }

        /// <summary>The <see cref="TreeSimulation.Step"/> tick the record belongs to.</summary>
        public uint Tick { get; }
        public AgentTraceFact Fact { get; }
        /// <summary>
        /// The record in the package's native trace format. Sequence numbers are the agent trace's own (1, 2, ...
        /// over the agent's whole trace), TreeInstanceId is the container-local instance id, UpdateId the
        /// instance's update count.
        /// </summary>
        public NativeTraceRecordV1 Record { get; }
        /// <summary>For <see cref="AgentTraceFact.Think"/>: why the agent thought.</summary>
        public ThinkReasons Reasons { get; }
        /// <summary>Input, Command, action facts: the channel id (FNV-1a 64 of the binding id, as <see cref="InputChannel{T}.Id"/>).</summary>
        public ulong Channel { get; }
        /// <summary>Action facts: the action's operation id (its node is <see cref="OperationId.NodeIndex"/>).</summary>
        public OperationId Operation { get; }
        /// <summary>ActionCancelled: why. Teardown: AgentRemoved, AgentRestarted or GroupRemoved.</summary>
        public ActionCancelReason CancelReason { get; }
        /// <summary>Fault: the failure (owner id zeroed, as in <see cref="TreeGroup.GetFaults"/>).</summary>
        public NativeRuntimeFailureV1 Failure { get; }
        /// <summary>Input, Command, ActionStarted, ActionCancelled: the canonical value's size in bytes (0 for other facts).</summary>
        public uint ValueSize { get; }
        /// <summary>Canonical value bytes 0-7 (little-endian), zero past <see cref="ValueSize"/>.</summary>
        public ulong ValueLow { get; }
        /// <summary>Canonical value bytes 8-15 (little-endian), zero past <see cref="ValueSize"/>.</summary>
        public ulong ValueHigh { get; }

        /// <summary>Canonical value byte <paramref name="index"/> (0-15); 0 past the kept bytes.</summary>
        public byte GetValueByte(int index)
        {
            if ((uint)index >= 16u || (uint)index >= ValueSize) return 0;
            return (byte)((index < 8 ? ValueLow >> (index * 8) : ValueHigh >> ((index - 8) * 8)) & 0xFF);
        }

        /// <summary>The kept value bytes as lowercase hex ("" when none). Allocates; for tools and logs.</summary>
        public string ValueHex()
        {
            var kept = (int)Math.Min(ValueSize, 16u);
            if (kept == 0) return "";
            var chars = new char[kept * 2];
            const string digits = "0123456789abcdef";
            for (var index = 0; index < kept; index++)
            {
                var value = GetValueByte(index);
                chars[index * 2] = digits[value >> 4];
                chars[index * 2 + 1] = digits[value & 0xF];
            }
            return new string(chars);
        }
    }

    /// <summary>
    /// P10-012: capacities of a group's agent traces, fixed until the next <see cref="TreeGroup.TryConfigureTracing"/>.
    /// All memory is allocated when an agent is first traced (or when configured), never in a step.
    /// Experimental until G5.
    /// </summary>
    public struct AgentTraceSettings
    {
        /// <summary>Agents of the group that can be traced at once (a pool of traces). Default 8.</summary>
        public int MaximumTracedAgents;
        /// <summary>Records each traced agent keeps (a ring: the oldest record is dropped and counted when full). Default 1024.</summary>
        public int RecordsPerAgent;
        /// <summary>
        /// Lifecycle records one think (or one teardown) of a traced agent can produce; later records of that
        /// think are dropped and counted. Default 256 (a think of the largest measured tree needs a few dozen).
        /// </summary>
        public int RecordsPerThink;

        public const int DefaultMaximumTracedAgents = 8;
        public const int DefaultRecordsPerAgent = 1024;
        public const int DefaultRecordsPerThink = 256;

        public static AgentTraceSettings Default => new AgentTraceSettings
        {
            MaximumTracedAgents = DefaultMaximumTracedAgents,
            RecordsPerAgent = DefaultRecordsPerAgent,
            RecordsPerThink = DefaultRecordsPerThink,
        };
    }

    /// <summary>P10-012: counters of one agent trace (<see cref="TreeGroup.TryCopyTrace"/>). Experimental until G5.</summary>
    public readonly struct AgentTraceInfo
    {
        internal AgentTraceInfo(int count, ulong recorded, ulong dropped, bool droppedIsLowerBound)
        {
            Count = count;
            Recorded = recorded;
            Dropped = dropped;
            DroppedIsLowerBound = droppedIsLowerBound;
        }

        /// <summary>Records in the ring now (at most <see cref="AgentTraceSettings.RecordsPerAgent"/>).</summary>
        public int Count { get; }
        /// <summary>Records written since tracing started (or the last clear), kept or not.</summary>
        public ulong Recorded { get; }
        /// <summary>Records lost: pushed out of the full ring, or beyond <see cref="AgentTraceSettings.RecordsPerThink"/> in one think.</summary>
        public ulong Dropped { get; }
        /// <summary>
        /// True when a think (or teardown) wrote more than four times <see cref="AgentTraceSettings.RecordsPerThink"/>
        /// lifecycle records since tracing started or the last clear: the per-think buffer stops counting there, so
        /// <see cref="Dropped"/> is a lower bound (each such think counts one drop for everything past the bound).
        /// </summary>
        public bool DroppedIsLowerBound { get; }
    }

    /// <summary>P10-012: where an agent is in its life (<see cref="AgentDebugState.State"/>). Experimental until G5.</summary>
    public enum AgentRunState : byte
    {
        /// <summary>Added (or reset to a fresh instance) and not thought yet.</summary>
        NotStarted = 0,
        /// <summary>Its tree is running: it holds an active path and thinks again on its schedule.</summary>
        Running = 1,
        /// <summary>
        /// Its root finished (<see cref="AgentDebugState.RootResult"/>). Under <see cref="RootCompletionPolicy.Stop"/> it
        /// stays so until <see cref="TreeGroup.TryRestart"/>; under Restart a fresh instance starts at its next think.
        /// </summary>
        Finished = 2,
        /// <summary>It faulted (<see cref="AgentDebugState.Fault"/>) and stays stopped until restarted or removed.</summary>
        Faulted = 3,
    }

    /// <summary>P10-012: one node of an agent's active path, root first (<see cref="TreeGroup.TryGetActivePath"/>). Experimental until G5.</summary>
    public readonly struct ActivePathNode
    {
        internal ActivePathNode(uint nodeIndex, ulong stableNodeId, ulong nodeTypeId, NodeStatus status)
        {
            NodeIndex = nodeIndex;
            StableNodeId = stableNodeId;
            NodeTypeId = nodeTypeId;
            Status = status;
        }

        /// <summary>The runtime node index in the compiled program (<see cref="CompiledProgram.Nodes"/>).</summary>
        public uint NodeIndex { get; }
        /// <summary>FNV-1a 64 of the authoring node id (stable across recompiles of the same tree); 0 when the program has no debug entry for the node.</summary>
        public ulong StableNodeId { get; }
        /// <summary>The node type id (FNV-1a 64 of the type id string, as <see cref="CompiledNodeRecord.NodeTypeId"/>).</summary>
        public ulong NodeTypeId { get; }
        /// <summary>Running for every node on the path: each one is active and waits for its child (the last one, the leaf, for the game or time).</summary>
        public NodeStatus Status { get; }
    }

    /// <summary>
    /// P10-012: "why is agent N doing X" without the path: its life state, last root result, fault, think
    /// times, actions and trace status (<see cref="TreeGroup.TryGetActivePath"/>). Experimental until G5.
    /// </summary>
    public readonly struct AgentDebugState
    {
        internal AgentDebugState(
            AgentHandle agent, AgentRunState state, ulong treeInstanceId, ulong updates,
            bool hasRootResult, NodeStatus rootResult, NativeRuntimeFailureV1 fault, BurstContextResult faultCause,
            bool hasThought, uint lastThinkTick, ThinkReasons lastThinkReasons, bool hasNextThink, uint nextThinkTick,
            bool wakePending, bool restartPending, bool freshInstancePending, uint thinkInterval, uint thinkPhase,
            int liveActionCount, bool isTraced, int pathDepth)
        {
            Agent = agent;
            State = state;
            TreeInstanceId = treeInstanceId;
            Updates = updates;
            HasRootResult = hasRootResult;
            RootResult = rootResult;
            Fault = fault;
            FaultCause = faultCause;
            HasThought = hasThought;
            LastThinkTick = lastThinkTick;
            LastThinkReasons = lastThinkReasons;
            HasNextThink = hasNextThink;
            NextThinkTick = nextThinkTick;
            WakePending = wakePending;
            RestartPending = restartPending;
            FreshInstancePending = freshInstancePending;
            ThinkInterval = thinkInterval;
            ThinkPhase = thinkPhase;
            LiveActionCount = liveActionCount;
            IsTraced = isTraced;
            PathDepth = pathDepth;
        }

        public AgentHandle Agent { get; }
        public AgentRunState State { get; }
        /// <summary>The container-local tree instance id (changes at every fresh instance).</summary>
        public ulong TreeInstanceId { get; }
        /// <summary>Updates the current instance ran (thinks plus teardowns).</summary>
        public ulong Updates { get; }
        /// <summary>True once the current instance's root finished; <see cref="RootResult"/> is then Success or Failure.</summary>
        public bool HasRootResult { get; }
        public NodeStatus RootResult { get; }
        /// <summary>The fault that stopped the agent (Code None when it did not fault); owner id zeroed.</summary>
        public NativeRuntimeFailureV1 Fault { get; }
        /// <summary>The callback result behind a CallbackRejected fault (as <see cref="AgentFault.Cause"/>), else Success.</summary>
        public BurstContextResult FaultCause { get; }
        /// <summary>False until the agent's first think since it was added.</summary>
        public bool HasThought { get; }
        public uint LastThinkTick { get; }
        public ThinkReasons LastThinkReasons { get; }
        /// <summary>False when the agent will not think again on its own (finished under Stop, or faulted) and no restart is pending.</summary>
        public bool HasNextThink { get; }
        /// <summary>The next tick on the agent's schedule after the container's last step (a pending wake makes it think in the next step anyway).</summary>
        public uint NextThinkTick { get; }
        /// <summary>The agent thinks in the next step whatever its schedule (woken by the game or by a completion).</summary>
        public bool WakePending { get; }
        /// <summary><see cref="TreeGroup.TryRestart"/> was called: the next step tears it down and its next think starts a fresh instance.</summary>
        public bool RestartPending { get; }
        /// <summary>Its next think starts a fresh instance (root finished under Restart, or restart pending).</summary>
        public bool FreshInstancePending { get; }
        public uint ThinkInterval { get; }
        public uint ThinkPhase { get; }
        /// <summary>Started actions not ended yet (<see cref="TreeGroup.TryCopyLiveActions"/>).</summary>
        public int LiveActionCount { get; }
        public bool IsTraced { get; }
        /// <summary>Nodes on the active path (what <see cref="TreeGroup.TryGetActivePath"/> wrote).</summary>
        public int PathDepth { get; }
    }

    /// <summary>P10-012: one live external action of an agent (<see cref="TreeGroup.TryCopyLiveActions"/>). Experimental until G5.</summary>
    public readonly struct LiveAction
    {
        internal LiveAction(ActionHandle action, bool completed, ActionOutcome outcome)
        {
            Action = action;
            Completed = completed;
            Outcome = outcome;
        }

        public ActionHandle Action { get; }
        /// <summary>The game completed it; the node reads the completion at the agent's next think.</summary>
        public bool Completed { get; }
        /// <summary>The completion's outcome when <see cref="Completed"/>.</summary>
        public ActionOutcome Outcome { get; }
    }
}
