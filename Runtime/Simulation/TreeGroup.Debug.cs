using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AIBT.Runtime.Scheduling;
using Unity.Collections;

namespace AIBT.Simulation
{
    /// <summary>
    /// P10-012 (ADR-P10-008 stage G4): tracing a few selected agents, and the "why is agent N doing X"
    /// query for any agent. Nothing here changes what agents do: traces only observe (the package's native
    /// trace recorder plus the container's own facts), queries only read, and untraced agents pay one
    /// test of their slot's trace index per think.
    /// </summary>
    public sealed partial class TreeGroup
    {
        private struct TracedCompletion
        {
            internal OperationId Operation;
            internal int Channel;
        }

        /// <summary>One pooled agent trace: a per-think scratch channel the native recorder writes, and the agent's ring.</summary>
        private sealed class TraceEntry
        {
            internal NativeTraceChannelOwnerV1 Channel;
            internal NativeTraceRecorderV1 Recorder;
            internal NativeArray<AgentTraceRecord> Ring;
            internal NativeList<TracedCompletion> Completed;
            internal int Head;
            internal int Count;
            internal ulong Recorded;
            internal ulong Dropped;
            internal bool Overrun;
            internal ulong Sequence;
            internal bool InUse;
            internal int Slot;
            internal uint Generation;
            internal bool Thinking;
            internal int CommandMark;
            internal int EventMark;
            internal bool HasPendingFault;
            internal NativeRuntimeFailureV1 PendingFault;

            internal void Dispose()
            {
                if (Channel != null && Channel.State != NativeOwnerStateV1.Disposed) Channel.TryDispose(out _);
                Channel = null;
                Recorder = null;
                if (Ring.IsCreated) Ring.Dispose();
                if (Completed.IsCreated) Completed.Dispose();
                InUse = false;
            }
        }

        private TraceEntry[] _traces = Array.Empty<TraceEntry>();
        private AgentTraceSettings _traceSettings = AgentTraceSettings.Default;
        private int _tracedCount;
        private uint _stepTick;
        private NativeArray<byte> _traceScratch;
        // Per program node, built once: FNV-1a 64 of the authoring id and the node type id.
        private ulong[] _stableNodeIds;
        private ulong[] _nodeTypeIds;
        private NativeHash256V1 _traceHash;

        /// <summary>The trace capacities in effect (<see cref="AgentTraceSettings.Default"/> until configured).</summary>
        public AgentTraceSettings TraceSettings => _traceSettings;

        /// <summary>Agents traced now (a removed agent's trace counts until it is untraced or its slot is reused).</summary>
        public int TracedCount => _tracedCount;

        /// <summary>
        /// Sets the trace capacities: agents traced at once, records kept per agent (a ring, oldest dropped
        /// first) and lifecycle records per think. Refused with <see cref="SimulationResult.InvalidArgument"/>
        /// for a value below 1 (or <see cref="AgentTraceSettings.RecordsPerThink"/> above 65 536) and while any
        /// agent is traced (untrace them first). Frees the previous pool; memory for each trace is allocated
        /// when an agent is first traced into it.
        /// </summary>
        public SimulationResult TryConfigureTracing(in AgentTraceSettings settings)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (settings.MaximumTracedAgents < 1 || settings.RecordsPerAgent < 1 || settings.RecordsPerThink < 1
                || settings.RecordsPerThink > 65536)
                return SimulationResult.InvalidArgument;
            if (_tracedCount != 0) return SimulationResult.InvalidArgument;
            for (var index = 0; index < _traces.Length; index++) _traces[index]?.Dispose();
            _traces = Array.Empty<TraceEntry>();
            _traceSettings = settings;
            return SimulationResult.Success;
        }

        /// <summary>
        /// Starts or stops tracing <paramref name="agent"/>. A traced agent records, at every think and
        /// teardown, why it thought, the input rows it read, every lifecycle event of its tree (the package's
        /// native trace records), the commands it emitted, its action events and consumed completions, and its
        /// fault; read them with <see cref="TryCopyTrace"/>. Tracing never changes results. Starting allocates
        /// the trace's memory the first time a pool entry is used (never in a step); returns
        /// <see cref="SimulationResult.CapacityExceeded"/> when <see cref="AgentTraceSettings.MaximumTracedAgents"/>
        /// agents are traced, <see cref="SimulationResult.StaleHandle"/> for an agent that is not alive.
        /// Tracing a traced agent, or stopping an untraced one, succeeds and changes nothing. Stopping also
        /// accepts the stale handle of a removed agent whose trace was kept (a removed agent's trace stays
        /// readable until it is stopped or its slot is reused).
        /// </summary>
        public SimulationResult TrySetTraced(AgentHandle agent, bool traced)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!traced)
            {
                if (TryFindTrace(agent, out var existing))
                {
                    ReleaseTrace(existing);
                    return SimulationResult.Success;
                }
                return TryResolve(agent, out _) ? SimulationResult.Success : SimulationResult.StaleHandle;
            }
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            ref var slot = ref _slots[index];
            if (slot.Trace != 0) return SimulationResult.Success;
            var free = -1;
            for (var position = 0; position < _traces.Length; position++)
                if (_traces[position] != null && !_traces[position].InUse) { free = position; break; }
            if (free < 0)
            {
                if (_traces.Length >= _traceSettings.MaximumTracedAgents) return SimulationResult.CapacityExceeded;
                var entry = CreateTraceEntry(_traces.Length);
                if (entry == null) return SimulationResult.InvalidArgument;
                var grown = new TraceEntry[_traces.Length + 1];
                Array.Copy(_traces, grown, _traces.Length);
                grown[_traces.Length] = entry;
                _traces = grown;
                free = _traces.Length - 1;
            }
            var trace = _traces[free];
            if (!slot.Core.TryAttachTrace(trace.Channel, trace.Recorder)) return SimulationResult.InvalidArgument;
            trace.Channel.TryReset(out _);
            trace.InUse = true;
            trace.Slot = index;
            trace.Generation = slot.Generation;
            trace.Head = 0;
            trace.Count = 0;
            trace.Recorded = 0;
            trace.Dropped = 0;
            trace.Overrun = false;
            trace.Sequence = 0;
            trace.Thinking = false;
            trace.HasPendingFault = false;
            trace.Completed.Clear();
            slot.Trace = free + 1;
            _tracedCount++;
            return SimulationResult.Success;
        }

        /// <summary>True while <paramref name="agent"/> (alive) is traced.</summary>
        public bool IsTraced(AgentHandle agent) => !_released && TryResolve(agent, out var index) && _slots[index].Trace != 0;

        /// <summary>
        /// Copies <paramref name="agent"/>'s trace into <paramref name="records"/> (cleared first), oldest
        /// first. Allocates nothing when the list's capacity is large enough. Accepts the stale handle of a
        /// removed agent whose trace was kept. Returns <see cref="SimulationResult.NotTraced"/> when the agent
        /// has no trace, <see cref="SimulationResult.InvalidArgument"/> for a list that is not created.
        /// </summary>
        public SimulationResult TryCopyTrace(AgentHandle agent, NativeList<AgentTraceRecord> records, out AgentTraceInfo info)
        {
            info = default;
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!records.IsCreated) return SimulationResult.InvalidArgument;
            if (!TryFindTrace(agent, out var position)) return TryResolve(agent, out _) ? SimulationResult.NotTraced : SimulationResult.StaleHandle;
            var trace = _traces[position];
            records.Clear();
            if (records.Capacity < trace.Count) records.Capacity = trace.Count;
            for (var index = 0; index < trace.Count; index++) records.Add(trace.Ring[(trace.Head + index) % trace.Ring.Length]);
            info = new AgentTraceInfo(trace.Count, trace.Recorded, trace.Dropped, trace.Overrun);
            return SimulationResult.Success;
        }

        /// <summary>Empties <paramref name="agent"/>'s trace and its counters; tracing continues.</summary>
        public SimulationResult TryClearTrace(AgentHandle agent)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!TryFindTrace(agent, out var position)) return TryResolve(agent, out _) ? SimulationResult.NotTraced : SimulationResult.StaleHandle;
            var trace = _traces[position];
            trace.Head = 0;
            trace.Count = 0;
            trace.Recorded = 0;
            trace.Dropped = 0;
            trace.Overrun = false;
            return SimulationResult.Success;
        }

        /// <summary>
        /// The handle of the live agent in slot <paramref name="index"/>, for tools that list agents. False for
        /// a free, removed or retired slot or an index outside the group.
        /// </summary>
        public bool TryGetAgentAt(int index, out AgentHandle agent)
        {
            agent = default;
            if (_released || (uint)index >= (uint)_slots.Length || _slots[index].State != SlotState.Alive) return false;
            agent = new AgentHandle(index, _slots[index].Generation, Id);
            return true;
        }

        // ---- "why is agent N doing X" ----------------------------------------------------------------

        /// <summary>
        /// Writes <paramref name="agent"/>'s active path, root first and the running leaf last, into
        /// <paramref name="path"/> (cleared first) and its state into <paramref name="state"/>: life state, last
        /// root result, fault with cause, when it last thought and why, when it thinks next, its live action
        /// count and whether it is traced. Works for every agent, traced or not, between steps. Allocates
        /// nothing when the list's capacity is large enough. An agent that finished or faulted has the path its
        /// machine holds (empty after its root finished). Returns <see cref="SimulationResult.StaleHandle"/> for
        /// a removed agent, <see cref="SimulationResult.InvalidArgument"/> for a list that is not created.
        /// </summary>
        public SimulationResult TryGetActivePath(AgentHandle agent, NativeList<ActivePathNode> path, out AgentDebugState state)
        {
            state = default;
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!path.IsCreated) return SimulationResult.InvalidArgument;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            path.Clear();
            var machine = _slots[index].Core.Machine;
            var depth = machine.ActiveDepth;
            if (path.Capacity < depth) path.Capacity = depth;
            for (var position = 0; position < depth; position++) path.Add(PathNode(machine.ActiveNodeAt(position)));
            state = BuildState(index, depth);
            return SimulationResult.Success;
        }

        /// <summary>
        /// As <see cref="TryGetActivePath(AgentHandle, NativeList{ActivePathNode}, out AgentDebugState)"/> into a
        /// caller span. Returns <see cref="SimulationResult.CapacityExceeded"/> when the path is longer than the
        /// span, with <paramref name="count"/> the length needed and nothing written.
        /// </summary>
        public SimulationResult TryGetActivePath(AgentHandle agent, Span<ActivePathNode> path, out int count, out AgentDebugState state)
        {
            count = 0;
            state = default;
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            var machine = _slots[index].Core.Machine;
            count = machine.ActiveDepth;
            if (count > path.Length) return SimulationResult.CapacityExceeded;
            for (var position = 0; position < count; position++) path[position] = PathNode(machine.ActiveNodeAt(position));
            state = BuildState(index, count);
            return SimulationResult.Success;
        }

        /// <summary>
        /// Copies <paramref name="agent"/>'s live external actions (started, not ended) into
        /// <paramref name="actions"/> (cleared first), in ledger order, each with whether the game completed it.
        /// </summary>
        public SimulationResult TryCopyLiveActions(AgentHandle agent, NativeList<LiveAction> actions)
        {
            var usable = Usable();
            if (usable != SimulationResult.Success) return usable;
            if (!actions.IsCreated) return SimulationResult.InvalidArgument;
            if (!TryResolve(agent, out var index)) return SimulationResult.StaleHandle;
            actions.Clear();
            var entry = _simAgents[index];
            for (var record = 0; record < entry.Actions.Length; record++)
            {
                var item = entry.Actions[record];
                actions.Add(new LiveAction(
                    new ActionHandle(agent, item.Operation, _map.Actions[item.Channel].Id), item.Completed != 0,
                    item.Outcome == AIBT.Burst.BurstCompletionOutcome.Failed ? ActionOutcome.Failed : ActionOutcome.Succeeded));
            }
            return SimulationResult.Success;
        }

        /// <summary>
        /// A readable account of <paramref name="agent"/>: life state, schedule, last and next think, the active
        /// path with node names from the program's debug map, root result, fault, live actions and, for a traced
        /// agent, each node's last result seen in its trace. A debug call: it allocates, and is meant for logs,
        /// consoles and tools, never for every frame. For a call that fails (stale handle, during a step) the
        /// text says why.
        /// </summary>
        public string DescribeAgent(AgentHandle agent) => DescribeAgent(agent, null);

        /// <summary>
        /// As <see cref="DescribeAgent(AgentHandle)"/>, with <paramref name="nodeTypeName"/> naming node type ids
        /// (null or a null result: core composites by name, other types as hex ids).
        /// </summary>
        public string DescribeAgent(AgentHandle agent, Func<ulong, string> nodeTypeName)
        {
            var path = new NativeList<ActivePathNode>(16, Allocator.Temp);
            var actions = new NativeList<LiveAction>(4, Allocator.Temp);
            try
            {
                var result = TryGetActivePath(agent, path, out var state);
                if (result != SimulationResult.Success) return agent + ": " + result;
                TryCopyLiveActions(agent, actions);
                var text = new StringBuilder(512);
                var invariant = CultureInfo.InvariantCulture;
                text.Append("Agent ").Append(agent.Group.ToString(invariant)).Append(':').Append(agent.Index.ToString(invariant))
                    .Append('#').Append(agent.Generation.ToString(invariant)).Append(" (").Append(ProgramName()).Append(") is ")
                    .Append(StateText(state)).Append(", instance ").Append(state.TreeInstanceId.ToString(invariant))
                    .Append(", ").Append(state.Updates.ToString(invariant)).Append(state.Updates == 1 ? " update" : " updates")
                    .Append(state.IsTraced ? ", traced" : "").Append('.').Append('\n');
                text.Append("Thinks every ").Append(state.ThinkInterval.ToString(invariant)).Append(state.ThinkInterval == 1 ? " tick" : " ticks")
                    .Append(" at phase ").Append(state.ThinkPhase.ToString(invariant)).Append("; ");
                if (state.HasThought)
                    text.Append("last think at tick ").Append(state.LastThinkTick.ToString(invariant)).Append(" (").Append(ReasonsText(state.LastThinkReasons)).Append(")");
                else text.Append("has not thought yet");
                if (state.HasNextThink)
                    text.Append("; next think at tick ").Append(state.NextThinkTick.ToString(invariant));
                else text.Append("; no next think until restarted");
                if (state.WakePending) text.Append(" (woken: thinks in the next step)");
                if (state.RestartPending) text.Append(" (restart pending)");
                else if (state.FreshInstancePending) text.Append(" (starts a fresh instance)");
                text.Append(".\n");
                if (path.Length == 0) text.Append("Active path: none.\n");
                else
                {
                    text.Append("Active path (root first):\n");
                    for (var position = 0; position < path.Length; position++)
                    {
                        var node = path[position];
                        text.Append("  ").Append(new string(' ', position * 2)).Append(NodeLabel(node.NodeIndex))
                            .Append(" [").Append(TypeName(node.NodeTypeId, nodeTypeName)).Append(", node ")
                            .Append(node.NodeIndex.ToString(invariant)).Append("] ").Append(node.Status).Append('\n');
                    }
                }
                text.Append("Last root result: ").Append(state.HasRootResult ? state.RootResult.ToString() : "none").Append(". Fault: ");
                if (state.Fault.Code == NativeRuntimeDiagnosticCodeV1.None) text.Append("none");
                else
                {
                    text.Append(state.Fault.Code).Append(" (AIBT").Append(((ushort)state.Fault.Code).ToString("0000", invariant)).Append(')');
                    if (state.FaultCause != AIBT.Burst.BurstContextResult.Success) text.Append(", cause ").Append(state.FaultCause);
                }
                text.Append(".\n");
                text.Append("Live actions: ").Append(actions.Length.ToString(invariant));
                for (var position = 0; position < actions.Length; position++)
                {
                    var item = actions[position];
                    var node = item.Action.Operation.NodeIndex.Value;
                    text.Append(position == 0 ? " - " : "; ").Append(NodeLabel(node)).Append(" (channel 0x")
                        .Append(item.Action.Channel.ToString("x16", invariant)).Append(", sequence ")
                        .Append(item.Action.Operation.Sequence.ToString(invariant)).Append(item.Completed ? ", completed " + item.Outcome + ", read at the next think" : ", waiting for the game").Append(')');
                }
                text.Append(".\n");
                if (state.IsTraced) AppendTraceSummary(agent, text, nodeTypeName);
                return text.ToString();
            }
            finally
            {
                path.Dispose();
                actions.Dispose();
            }
        }

        private void AppendTraceSummary(AgentHandle agent, StringBuilder text, Func<ulong, string> nodeTypeName)
        {
            if (!TryFindTrace(agent, out var position)) return;
            var trace = _traces[position];
            var invariant = CultureInfo.InvariantCulture;
            text.Append("Trace: ").Append(trace.Count.ToString(invariant)).Append(" records kept, ")
                .Append(trace.Recorded.ToString(invariant)).Append(" written, ").Append(trace.Dropped.ToString(invariant)).Append(trace.Overrun ? " or more dropped.\n" : " dropped.\n");
            // Each node's last result in the kept trace (exit status, or the last tick status of a leaf).
            var last = new Dictionary<uint, (uint Tick, string Result)>();
            var order = new List<uint>();
            for (var index = 0; index < trace.Count; index++)
            {
                var item = trace.Ring[(trace.Head + index) % trace.Ring.Length];
                if (item.Fact != AgentTraceFact.Lifecycle) continue;
                var record = item.Record;
                if ((record.OptionalFields & NativeTraceOptionalFieldsV1.RuntimeNode) == 0) continue;
                string result;
                if (record.Kind == NativeTraceEventKindV1.NodeExited)
                    result = (record.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) != 0 ? "exited " + record.ExitReason : "exited";
                else if (record.Kind == NativeTraceEventKindV1.NodeTicked && (record.OptionalFields & NativeTraceOptionalFieldsV1.Status) != 0)
                    result = "ticked " + record.Status;
                else if (record.Kind == NativeTraceEventKindV1.NodeAbortStarted)
                    result = (record.OptionalFields & NativeTraceOptionalFieldsV1.AbortReason) != 0 ? "aborted (" + record.AbortReason + ")" : "aborted";
                else continue;
                if (!last.ContainsKey(record.RuntimeNodeIndex)) order.Add(record.RuntimeNodeIndex);
                last[record.RuntimeNodeIndex] = (item.Tick, result);
            }
            if (order.Count == 0) return;
            order.Sort();
            text.Append("Last results in the trace:\n");
            foreach (var node in order)
            {
                var entry = last[node];
                text.Append("  ").Append(NodeLabel(node)).Append(" [").Append(TypeName(_nodeTypeIds[node], nodeTypeName)).Append("] ")
                    .Append(entry.Result).Append(" at tick ").Append(entry.Tick.ToString(invariant)).Append('\n');
            }
        }

        private static string StateText(in AgentDebugState state)
        {
            switch (state.State)
            {
                case AgentRunState.NotStarted: return "not started";
                case AgentRunState.Running: return "running";
                case AgentRunState.Finished: return "finished (" + state.RootResult + ")";
                default: return "faulted";
            }
        }

        private static string ReasonsText(ThinkReasons reasons)
        {
            if (reasons == ThinkReasons.None) return "no reason recorded";
            var parts = new List<string>(5);
            if ((reasons & ThinkReasons.Scheduled) != 0) parts.Add("scheduled");
            if ((reasons & ThinkReasons.WokenByGame) != 0) parts.Add("woken by the game");
            if ((reasons & ThinkReasons.InputChanged) != 0) parts.Add("woken by an input change");
            if ((reasons & ThinkReasons.ActionCompleted) != 0) parts.Add("woken by an action completion");
            if ((reasons & ThinkReasons.FreshInstance) != 0) parts.Add("fresh instance");
            return string.Join(", ", parts);
        }

        private string ProgramName()
        {
            var map = _program.Program.DebugMap;
            return map.Count != 0 ? map[0].SourcePath : "program " + _program.Program.Header.CanonicalSemanticHash.HexadecimalValue.Substring(0, 12);
        }

        /// <summary>The node's display name, else its authoring id, else "node N".</summary>
        internal string NodeLabel(uint node)
        {
            var map = _program.Program.DebugMap;
            for (var index = 0; index < map.Count; index++)
            {
                if (map[index].RuntimeNodeIndex != node) continue;
                return !string.IsNullOrEmpty(map[index].DisplayName) ? map[index].DisplayName : map[index].AuthoringNodeId.Value;
            }
            return "node " + node.ToString(CultureInfo.InvariantCulture);
        }

        private static readonly string[] CoreTypeNames =
        {
            "aibt.core.memory-sequence", "aibt.core.memory-selector", "aibt.core.reactive-sequence", "aibt.core.reactive-selector",
            "aibt.core.inverter", "aibt.core.succeeder", "aibt.core.failer", "aibt.core.repeater", "aibt.core.timeout",
            "aibt.core.cooldown", "aibt.core.parallel",
            "aibt.core.reactive-sequence-retaining", "aibt.core.reactive-selector-retaining",
        };

        internal static string TypeName(ulong typeId, Func<ulong, string> nodeTypeName)
        {
            var named = nodeTypeName?.Invoke(typeId);
            if (!string.IsNullOrEmpty(named)) return named;
            for (var index = 0; index < CoreTypeNames.Length; index++)
                if (StableHash.Fnv1A64(CoreTypeNames[index]) == typeId) return CoreTypeNames[index];
            return "type 0x" + typeId.ToString("x16", CultureInfo.InvariantCulture);
        }

        private ActivePathNode PathNode(uint node)
        {
            var known = node < (uint)_nodeTypeIds.Length;
            return new ActivePathNode(node, known ? _stableNodeIds[node] : 0, known ? _nodeTypeIds[node] : 0, NodeStatus.Running);
        }

        private AgentDebugState BuildState(int index, int depth)
        {
            ref var slot = ref _slots[index];
            var core = slot.Core;
            var failure = core.LastFailure;
            var faulted = failure.Code != NativeRuntimeDiagnosticCodeV1.None;
            var reported = faulted
                ? new NativeRuntimeFailureV1(failure.Code, failure.ResourceKind, failure.Requested, failure.Capacity, failure.Alignment,
                    0, failure.Generation, failure.LeaseId)
                : default;
            var finished = !faulted && !core.Ready && core.LastRootResult.HasValue;
            var runState = faulted ? AgentRunState.Faulted
                : finished ? AgentRunState.Finished
                : core.TotalUpdates == 0 ? AgentRunState.NotStarted : AgentRunState.Running;
            var willThink = core.Ready || slot.ResetAtNextThink || slot.RestartRequested;
            var from = _simulation.HasStepped ? (ulong)_simulation.LastTick + 1 : 0ul;
            // A woken agent thinks in the next step; else at its next scheduled tick.
            var next = slot.Woken ? from : from + (slot.Phase + slot.Interval - from % slot.Interval) % slot.Interval;
            var hasNext = willThink && next <= uint.MaxValue;
            return new AgentDebugState(
                new AgentHandle(index, slot.Generation, Id), runState, core.InstanceId, core.TotalUpdates,
                core.LastRootResult.HasValue, core.LastRootResult ?? default, reported,
                faulted ? core.LastRejectedResult : AIBT.Burst.BurstContextResult.Success,
                slot.HasThought, slot.LastThinkTick, slot.LastReasons, hasNext, hasNext ? (uint)next : 0u,
                slot.Woken, slot.RestartRequested, slot.ResetAtNextThink || slot.RestartRequested, slot.Interval, slot.Phase,
                _simAgents[index].Actions.Length, slot.Trace != 0, depth);
        }

        /// <summary>Per program node: stable ids and type ids, built once when the group is created.</summary>
        private void BuildNodeTables()
        {
            var program = _program.Program;
            var count = program.Nodes.Count;
            _stableNodeIds = new ulong[count];
            _nodeTypeIds = new ulong[count];
            for (var node = 0; node < count; node++) _nodeTypeIds[node] = program.Nodes[node].NodeTypeId;
            var map = program.DebugMap;
            for (var index = 0; index < map.Count; index++)
            {
                var node = map[index].RuntimeNodeIndex;
                if (node < (uint)count) _stableNodeIds[node] = StableHash.Fnv1A64(map[index].AuthoringNodeId.Value);
            }
            _traceHash = new NativeHash256V1(program.Header.CompiledContentHash);
        }

        // ---- trace recording (inside Step) -----------------------------------------------------------

        private TraceEntry CreateTraceEntry(int position)
        {
            var perThink = (uint)_traceSettings.RecordsPerThink;
            // The scratch channel holds one think's records (+1 for the drop summary); its instance id is a
            // fixed placeholder, replaced by the agent's container-local id when records are copied out.
            var capacity = new NativeTraceChannelCapacityV1(perThink + 1, 0, 0, perThink * 4);
            var placeholder = new TreeInstanceId(ulong.MaxValue - 1 - (ulong)position);
            if (!NativeTraceChannelOwnerV1.TryCreate(capacity, NativeTraceLevelV1.Detailed, placeholder, 0, Allocator.Persistent, out var channel, out _))
                return null;
            if (!_traceScratch.IsCreated)
            {
                var largest = 16;
                for (var input = 0; input < _map.Inputs.Count; input++) largest = Math.Max(largest, (int)_map.Inputs[input].CanonicalSize);
                _traceScratch = new NativeArray<byte>(largest, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }
            return new TraceEntry
            {
                Channel = channel,
                Recorder = new NativeTraceRecorderV1(channel, _traceHash, placeholder.Value),
                Ring = new NativeArray<AgentTraceRecord>(_traceSettings.RecordsPerAgent, Allocator.Persistent, NativeArrayOptions.ClearMemory),
                Completed = new NativeList<TracedCompletion>(Math.Max(1, _actionCapacity), Allocator.Persistent),
            };
        }

        /// <summary>The pool entry holding the trace of <paramref name="agent"/> (alive, or removed and kept).</summary>
        private bool TryFindTrace(AgentHandle agent, out int position)
        {
            position = -1;
            if (agent.Generation == 0 || agent.Group != Id || (uint)agent.Index >= (uint)_slots.Length) return false;
            var trace = _slots[agent.Index].Trace;
            if (trace == 0 || _traces[trace - 1].Generation != agent.Generation) return false;
            position = trace - 1;
            return true;
        }

        private void ReleaseTrace(int position)
        {
            var trace = _traces[position];
            if (!trace.InUse) return;
            if ((uint)trace.Slot < (uint)_slots.Length)
            {
                _slots[trace.Slot].Core.TryDetachTrace();
                _slots[trace.Slot].Trace = 0;
            }
            trace.InUse = false;
            trace.Thinking = false;
            trace.HasPendingFault = false;
            trace.Channel.TryReset(out _);
            _tracedCount--;
        }

        private void ReleaseTraces()
        {
            for (var position = 0; position < _traces.Length; position++)
            {
                var trace = _traces[position];
                if (trace == null) continue;
                if (trace.InUse && (uint)trace.Slot < (uint)_slots.Length)
                {
                    _slots[trace.Slot].Core?.TryDetachTrace();
                    _slots[trace.Slot].Trace = 0;
                }
                trace.Dispose();
            }
            _traces = Array.Empty<TraceEntry>();
            _tracedCount = 0;
            if (_traceScratch.IsCreated) _traceScratch.Dispose();
        }

        private void Append(TraceEntry trace, AgentTraceFact fact, NativeTraceRecordV1 record, ThinkReasons reasons = ThinkReasons.None,
            ulong channel = 0, OperationId operation = default, ActionCancelReason cancelReason = default,
            NativeRuntimeFailureV1 failure = default, uint valueSize = 0, ulong low = 0, ulong high = 0)
        {
            trace.Sequence++;
            record.Sequence = trace.Sequence;
            var item = new AgentTraceRecord(_stepTick, fact, record, reasons, channel, operation, cancelReason, failure, valueSize, low, high);
            var capacity = trace.Ring.Length;
            if (trace.Count < capacity)
            {
                trace.Ring[(trace.Head + trace.Count) % capacity] = item;
                trace.Count++;
            }
            else
            {
                trace.Ring[trace.Head] = item;
                trace.Head = (trace.Head + 1) % capacity;
                trace.Dropped++;
            }
            trace.Recorded++;
        }

        private NativeTraceRecordV1 FactRecord(
            ProductionTreeAgentCore core, ulong updateId, NativeTraceEventKindV1 kind, NativeUpdatePhaseV1 phase, uint node, ulong decision)
        {
            var record = new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = phase,
                UpdateId = updateId == 0 ? 1 : updateId,
                SnapshotRevision = 1,
                TreeSemanticHash = _traceHash,
                TreeInstanceId = core.InstanceId,
                Kind = kind,
                RuntimeNodeIndex = CompiledIndex.Invalid,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
                OperationOrDecisionId = decision,
            };
            if (node != CompiledIndex.Invalid)
            {
                record.OptionalFields |= NativeTraceOptionalFieldsV1.RuntimeNode;
                record.RuntimeNodeIndex = node;
            }
            return record;
        }

        /// <summary>Little-endian bytes [offset, offset + size) of <paramref name="bytes"/>, up to 16, as two words.</summary>
        private static void Pack(NativeArray<byte> bytes, int offset, uint size, out ulong low, out ulong high)
        {
            low = 0;
            high = 0;
            var kept = (int)Math.Min(size, 16u);
            for (var index = 0; index < kept; index++)
            {
                ulong value = bytes[offset + index];
                if (index < 8) low |= value << (index * 8);
                else high |= value << ((index - 8) * 8);
            }
        }

        private static void Pack(Unity.Collections.LowLevel.Unsafe.UnsafeList<byte> bytes, int offset, uint size, out ulong low, out ulong high)
        {
            low = 0;
            high = 0;
            var kept = (int)Math.Min(size, 16u);
            for (var index = 0; index < kept && offset + index < bytes.Length; index++)
            {
                ulong value = bytes[offset + index];
                if (index < 8) low |= value << (index * 8);
                else high |= value << ((index - 8) * 8);
            }
        }

        /// <summary>Teardown block start: the Teardown fact and the output marks.</summary>
        private void TraceTeardownStarted(int position, int index, ActionCancelReason reason)
        {
            var trace = _traces[position];
            var core = _slots[index].Core;
            Append(trace, AgentTraceFact.Teardown,
                FactRecord(core, core.TotalUpdates + 1, NativeTraceEventKindV1.SchedulerDecision, NativeUpdatePhaseV1.SelectWork, CompiledIndex.Invalid, 0),
                cancelReason: reason);
            ref var entry = ref _simAgents.ElementAt(index);
            trace.CommandMark = entry.Commands.Length;
            trace.EventMark = entry.Events.Length;
        }

        /// <summary>Teardown block end: its lifecycle records, commands and action events.</summary>
        private void TraceTeardownEnded(int position, int index)
        {
            var trace = _traces[position];
            var core = _slots[index].Core;
            FlushLifecycle(trace, core);
            FlushOutputs(trace, index, core);
        }

        /// <summary>Think block start: the Think fact, the input rows, the marks and the completions the think may consume.</summary>
        private void TraceThinkStarted(int position, int index, ThinkReasons reasons)
        {
            var trace = _traces[position];
            var core = _slots[index].Core;
            var updateId = core.TotalUpdates + 1;
            Append(trace, AgentTraceFact.Think,
                FactRecord(core, updateId, NativeTraceEventKindV1.SchedulerDecision, NativeUpdatePhaseV1.SelectWork, CompiledIndex.Invalid, (ulong)reasons),
                reasons);
            ref var entry = ref _simAgents.ElementAt(index);
            for (var channel = 0; channel < _inputs.Length; channel++)
            {
                var size = _inputs[channel].CanonicalSize;
                SimulationAgentRoutinesV1.TryStage(ref entry, channel, _traceScratch, 0u, size);
                Pack(_traceScratch, 0, size, out var low, out var high);
                var id = _map.Inputs[channel].Id;
                Append(trace, AgentTraceFact.Input,
                    FactRecord(core, updateId, NativeTraceEventKindV1.SchedulerDecision, NativeUpdatePhaseV1.CollectInput, CompiledIndex.Invalid, id),
                    channel: id, valueSize: size, low: low, high: high);
            }
            trace.CommandMark = entry.Commands.Length;
            trace.EventMark = entry.Events.Length;
            trace.Completed.Clear();
            for (var record = 0; record < entry.Actions.Length; record++)
            {
                var item = entry.Actions[record];
                if (item.Completed != 0) trace.Completed.Add(new TracedCompletion { Operation = item.Operation, Channel = item.Channel });
            }
            trace.Thinking = true;
        }

        private void TraceFault(int position, NativeRuntimeFailureV1 failure)
        {
            var trace = _traces[position];
            trace.HasPendingFault = true;
            trace.PendingFault = failure;
        }

        /// <summary>After every think of the group: each traced agent that thought or faulted closes its think block.</summary>
        private void TraceThinksEnded()
        {
            for (var position = 0; position < _traces.Length; position++)
            {
                var trace = _traces[position];
                if (trace == null || !trace.InUse || (!trace.Thinking && !trace.HasPendingFault)) continue;
                var index = trace.Slot;
                var core = _slots[index].Core;
                FlushLifecycle(trace, core);
                if (trace.Thinking)
                {
                    FlushOutputs(trace, index, core);
                    FlushConsumed(trace, index, core);
                }
                if (trace.HasPendingFault)
                {
                    var failure = trace.PendingFault;
                    var record = FactRecord(core, core.TotalUpdates, NativeTraceEventKindV1.DiagnosticRaised, NativeUpdatePhaseV1.Execute, CompiledIndex.Invalid, 0);
                    var number = (ushort)failure.Code;
                    if (number >= 1 && number <= 9999)
                    {
                        record.OptionalFields |= NativeTraceOptionalFieldsV1.DiagnosticCode;
                        record.DiagnosticCodeNumber = number;
                    }
                    Append(trace, AgentTraceFact.Fault, record, failure: failure);
                }
                trace.Thinking = false;
                trace.HasPendingFault = false;
            }
        }

        /// <summary>Moves the scratch channel's records (this think's or teardown's lifecycle) into the ring.</summary>
        private void FlushLifecycle(TraceEntry trace, ProductionTreeAgentCore core)
        {
            if (!trace.Channel.TryGetSnapshot(out var snapshot, out _)) return;
            if (snapshot.RecordCount == 0 && snapshot.DroppedCount == 0 && !snapshot.IsFaulted) return;
            var records = snapshot.Records;
            for (var index = 0; index < (int)snapshot.RecordCount; index++)
            {
                var record = records[index];
                if (record.Kind == NativeTraceEventKindV1.TraceDroppedSummary) continue;
                record.TreeInstanceId = core.InstanceId;
                Append(trace, AgentTraceFact.Lifecycle, record);
            }
            // A faulted channel (more appends than its emission capacity) lost at least one record uncounted.
            trace.Dropped += snapshot.DroppedCount + (snapshot.IsFaulted ? 1ul : 0ul);
            if (snapshot.IsFaulted) trace.Overrun = true;
            trace.Channel.TryReset(out _);
        }

        /// <summary>Commands and action events the agent produced since the block's marks.</summary>
        private void FlushOutputs(TraceEntry trace, int index, ProductionTreeAgentCore core)
        {
            ref var entry = ref _simAgents.ElementAt(index);
            var updateId = core.TotalUpdates;
            for (var position = trace.CommandMark; position < entry.Commands.Length; position++)
            {
                var command = entry.Commands[position];
                var id = (uint)command.Channel < (uint)_map.Commands.Count ? _map.Commands[command.Channel].Id : 0;
                Pack(entry.Payload, (int)command.PayloadOffset, command.PayloadSize, out var low, out var high);
                Append(trace, AgentTraceFact.Command,
                    FactRecord(core, updateId, NativeTraceEventKindV1.CommandEmitted, NativeUpdatePhaseV1.PublishCommands, CompiledIndex.Invalid, id),
                    channel: id, valueSize: command.PayloadSize, low: low, high: high);
            }
            trace.CommandMark = entry.Commands.Length;
            for (var position = trace.EventMark; position < entry.Events.Length; position++)
            {
                var item = entry.Events[position];
                var id = (uint)item.Channel < (uint)_map.Actions.Count ? _map.Actions[item.Channel].Id : 0;
                Pack(entry.EventPayload, (int)item.PayloadOffset, item.PayloadSize, out var low, out var high);
                var started = item.Kind == ActionEventKind.Started;
                Append(trace, started ? AgentTraceFact.ActionStarted : AgentTraceFact.ActionCancelled,
                    FactRecord(core, updateId,
                        started ? NativeTraceEventKindV1.CommandEmitted : NativeTraceEventKindV1.CompletionDiscarded,
                        NativeUpdatePhaseV1.ApplyIntegrations, item.Operation.NodeIndex.Value, id),
                    channel: id, operation: item.Operation, cancelReason: started ? default : item.Reason,
                    valueSize: item.PayloadSize, low: low, high: high);
            }
            trace.EventMark = entry.Events.Length;
        }

        /// <summary>Completions the game gave before the think whose actions ended in it without a Cancelled event: their nodes read them.</summary>
        private void FlushConsumed(TraceEntry trace, int index, ProductionTreeAgentCore core)
        {
            ref var entry = ref _simAgents.ElementAt(index);
            for (var position = 0; position < trace.Completed.Length; position++)
            {
                var completed = trace.Completed[position];
                if (SimulationActionRoutinesV1.Find(ref entry, completed.Operation) >= 0) continue;
                var cancelled = false;
                for (var item = 0; item < entry.Events.Length && !cancelled; item++)
                    cancelled = entry.Events[item].Kind == ActionEventKind.Cancelled && entry.Events[item].Operation == completed.Operation;
                if (cancelled) continue;
                var id = (uint)completed.Channel < (uint)_map.Actions.Count ? _map.Actions[completed.Channel].Id : 0;
                Append(trace, AgentTraceFact.CompletionConsumed,
                    FactRecord(core, core.TotalUpdates, NativeTraceEventKindV1.CompletionConsumed, NativeUpdatePhaseV1.Execute,
                        completed.Operation.NodeIndex.Value, id),
                    channel: id, operation: completed.Operation);
            }
            trace.Completed.Clear();
        }
    }
}
