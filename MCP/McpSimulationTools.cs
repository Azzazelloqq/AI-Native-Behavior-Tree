using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AIBT.Editor.Registry;
using AIBT.Mcp.Authoring;
using AIBT.Simulation;
using Newtonsoft.Json.Linq;
using Unity.Collections;

namespace AIBT.Mcp
{
    /// <summary>
    /// P10-012 (ADR-P10-008 stage G4): read-only tools over the simulation containers the game registered
    /// with <see cref="TreeSimulationDebug.Register"/>. Nothing is found unless the game opted in; nothing
    /// here changes a container (no step, no tracing switch, no completion). Calls during a step answer
    /// busy=true, as read_trace answers available=false while a host drives.
    /// </summary>
    internal static class McpSimulationTools
    {
        internal const string NothingRegistered =
            "No simulation container is registered. The game opts in with TreeSimulationDebug.Register(simulation, name) (Editor and development builds only).";

        internal static JObject Simulations()
        {
            var registered = new List<RegisteredSimulation>();
            TreeSimulationDebug.CopyRegistered(registered);
            var result = new JObject { ["simulations"] = new JArray(registered.Select(SimulationInfo)) };
            if (registered.Count == 0) result["hint"] = NothingRegistered;
            return result;
        }

        internal static JObject DescribeAgent(JObject args)
        {
            var (registered, group, agent) = Resolve(args);
            var result = AgentIdentity(registered, group, agent);
            if (registered.Simulation.IsStepping)
            {
                result["busy"] = true;
                return result;
            }
            var path = new NativeList<ActivePathNode>(16, Allocator.Temp);
            var actions = new NativeList<LiveAction>(4, Allocator.Temp);
            try
            {
                var status = group.TryGetActivePath(agent, path, out var state);
                if (status != SimulationResult.Success) throw Invalid("describe_agent: " + status + ". Use runtime_simulations for live agents.");
                group.TryCopyLiveActions(agent, actions);
                var names = TypeNames();
                result["busy"] = false;
                result["state"] = state.State.ToString();
                result["treeInstanceId"] = state.TreeInstanceId;
                result["updates"] = state.Updates;
                result["rootResult"] = state.HasRootResult ? state.RootResult.ToString() : null;
                result["fault"] = state.Fault.Code == NativeRuntimeDiagnosticCodeV1.None ? null : new JObject
                {
                    ["code"] = state.Fault.Code.ToString(),
                    ["number"] = (int)(ushort)state.Fault.Code,
                    ["cause"] = state.FaultCause.ToString(),
                };
                result["schedule"] = new JObject { ["interval"] = state.ThinkInterval, ["phase"] = state.ThinkPhase };
                result["lastThink"] = state.HasThought
                    ? new JObject { ["tick"] = state.LastThinkTick, ["reasons"] = Reasons(state.LastThinkReasons) }
                    : null;
                result["nextThinkTick"] = state.HasNextThink ? (JToken)state.NextThinkTick : null;
                result["wakePending"] = state.WakePending;
                result["restartPending"] = state.RestartPending;
                result["freshInstancePending"] = state.FreshInstancePending;
                result["traced"] = state.IsTraced;
                result["activePath"] = new JArray(Enumerable.Range(0, path.Length).Select(position =>
                {
                    var node = path[position];
                    return new JObject
                    {
                        ["nodeIndex"] = node.NodeIndex,
                        ["nodeId"] = group.NodeLabel(node.NodeIndex),
                        ["stableNodeId"] = node.StableNodeId.ToString("x16", CultureInfo.InvariantCulture),
                        ["typeId"] = TreeGroup.TypeName(node.NodeTypeId, names),
                        ["status"] = node.Status.ToString(),
                    };
                }));
                result["liveActions"] = new JArray(Enumerable.Range(0, actions.Length).Select(position =>
                {
                    var item = actions[position];
                    return new JObject
                    {
                        ["nodeIndex"] = item.Action.Operation.NodeIndex.Value,
                        ["nodeId"] = group.NodeLabel(item.Action.Operation.NodeIndex.Value),
                        ["channel"] = item.Action.Channel.ToString("x16", CultureInfo.InvariantCulture),
                        ["sequence"] = item.Action.Operation.Sequence,
                        ["completed"] = item.Completed,
                        ["outcome"] = item.Completed ? item.Outcome.ToString() : null,
                    };
                }));
                result["text"] = group.DescribeAgent(agent, names);
                return result;
            }
            finally
            {
                path.Dispose();
                actions.Dispose();
            }
        }

        internal static JObject ReadAgentTrace(JObject args)
        {
            var (registered, group, agent) = Resolve(args);
            var result = AgentIdentity(registered, group, agent);
            if (registered.Simulation.IsStepping)
            {
                result["available"] = false;
                result["busy"] = true;
                return result;
            }
            var limit = (int?)args["limit"] ?? 128;
            if (limit < 1 || limit > 512) throw Invalid("limit must be 1..512.");
            var after = (ulong?)args["afterSequence"] ?? 0;
            var records = new NativeList<AgentTraceRecord>(group.TraceSettings.RecordsPerAgent, Allocator.Temp);
            try
            {
                var status = group.TryCopyTrace(agent, records, out var info);
                if (status == SimulationResult.NotTraced)
                {
                    result["available"] = false;
                    result["traced"] = false;
                    result["reason"] = "The agent is not traced. The game selects agents with TreeGroup.TrySetTraced(agent, true).";
                    return result;
                }
                if (status != SimulationResult.Success) throw Invalid("read_agent_trace: " + status + ". Use runtime_simulations for live agents.");
                var names = TypeNames();
                var kept = new List<AgentTraceRecord>();
                for (var index = 0; index < records.Length; index++)
                    if (records[index].Record.Sequence > after) kept.Add(records[index]);
                var page = kept.Take(limit).ToList();
                result["available"] = true;
                result["traced"] = true;
                result["recorded"] = info.Recorded;
                result["dropped"] = info.Dropped;
                result["droppedIsLowerBound"] = info.DroppedIsLowerBound;
                result["complete"] = info.Dropped == 0;
                result["hasMore"] = kept.Count > page.Count;
                result["nextSequence"] = page.Count == 0 ? after : page[page.Count - 1].Record.Sequence;
                result["events"] = new JArray(page.Select(item => TraceEvent(group, item, names)));
                return result;
            }
            finally
            {
                records.Dispose();
            }
        }

        private static JObject TraceEvent(TreeGroup group, in AgentTraceRecord item, Func<ulong, string> names)
        {
            var record = item.Record;
            var hasNode = (record.OptionalFields & NativeTraceOptionalFieldsV1.RuntimeNode) != 0;
            var json = new JObject
            {
                ["sequence"] = record.Sequence,
                ["tick"] = item.Tick,
                ["fact"] = item.Fact.ToString(),
                ["kind"] = record.Kind.ToString(),
                ["updateId"] = record.UpdateId,
                ["nodeIndex"] = hasNode ? (JToken)record.RuntimeNodeIndex : null,
                ["nodeId"] = hasNode ? group.NodeLabel(record.RuntimeNodeIndex) : null,
                ["status"] = (record.OptionalFields & NativeTraceOptionalFieldsV1.Status) != 0 ? record.Status.ToString() : null,
                ["exitReason"] = (record.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) != 0 ? record.ExitReason.ToString() : null,
                ["abortReason"] = (record.OptionalFields & NativeTraceOptionalFieldsV1.AbortReason) != 0 ? record.AbortReason.ToString() : null,
            };
            switch (item.Fact)
            {
                case AgentTraceFact.Think:
                    json["reasons"] = Reasons(item.Reasons);
                    break;
                case AgentTraceFact.Input:
                case AgentTraceFact.Command:
                    json["channel"] = item.Channel.ToString("x16", CultureInfo.InvariantCulture);
                    json["valueSize"] = item.ValueSize;
                    json["value"] = item.ValueHex();
                    // P12-002: the row read (the slot, or an indexed channel's index; -1 = none, zeros read).
                    if (item.Fact == AgentTraceFact.Input) json["row"] = item.InputRow;
                    break;
                case AgentTraceFact.ActionStarted:
                case AgentTraceFact.ActionCancelled:
                case AgentTraceFact.CompletionConsumed:
                    json["channel"] = item.Channel.ToString("x16", CultureInfo.InvariantCulture);
                    json["operationSequence"] = item.Operation.Sequence;
                    if (item.Fact == AgentTraceFact.ActionCancelled) json["cancelReason"] = item.CancelReason.ToString();
                    if (item.Fact != AgentTraceFact.CompletionConsumed)
                    {
                        json["valueSize"] = item.ValueSize;
                        json["value"] = item.ValueHex();
                    }
                    break;
                case AgentTraceFact.Teardown:
                    json["reason"] = item.CancelReason.ToString();
                    break;
                case AgentTraceFact.Fault:
                    json["fault"] = item.Failure.Code.ToString();
                    json["diagnosticCodeNumber"] = record.DiagnosticCodeNumber;
                    break;
            }
            return json;
        }

        private static (RegisteredSimulation, TreeGroup, AgentHandle) Resolve(JObject args)
        {
            var registered = new List<RegisteredSimulation>();
            if (TreeSimulationDebug.CopyRegistered(registered) == 0)
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, NothingRegistered);
            var name = (string)args["simulation"];
            var matches = registered.Where(item => item.Name == name).ToList();
            if (matches.Count != 1)
                throw Invalid("Select one registered simulation by name (" + string.Join(", ", registered.Select(item => "'" + item.Name + "'")) + ").");
            var simulation = matches[0];
            var groups = new List<TreeGroup>();
            simulation.Simulation.CopyGroups(groups);
            var groupId = (int?)args["group"] ?? -1;
            var group = groups.FirstOrDefault(item => item.Id == groupId && item.IsValid);
            if (group == null) throw Invalid("Select a live group id of '" + name + "' from runtime_simulations.");
            var index = (int?)args["agent"] ?? -1;
            if (!group.TryGetAgentAt(index, out var agent))
            {
                // A removed agent's kept trace is still readable by its exact handle.
                var generation = (uint?)args["generation"] ?? 0u;
                if (generation == 0 || index < 0) throw Invalid("Select a live agent slot index of group " + groupId + " (0.." + (group.HighWaterIndex - 1) + ").");
                agent = AgentHandleFactory(index, generation, group.Id);
            }
            else if (args["generation"] != null && (uint)args["generation"] != agent.Generation)
                throw Invalid("Agent slot " + index + " now holds generation " + agent.Generation + ", not " + (uint)args["generation"] + ".");
            return (simulation, group, agent);
        }

        private static AgentHandle AgentHandleFactory(int index, uint generation, ushort group) => new AgentHandle(index, generation, group);

        private static JObject SimulationInfo(RegisteredSimulation registered)
        {
            var simulation = registered.Simulation;
            var groups = new List<TreeGroup>();
            simulation.CopyGroups(groups);
            return new JObject
            {
                ["name"] = registered.Name,
                ["lastTick"] = simulation.LastTick,
                ["stepping"] = simulation.IsStepping,
                ["tickMicroseconds"] = simulation.Settings.TickMicroseconds,
                ["groups"] = new JArray(groups.Select(group =>
                {
                    var info = new JObject
                    {
                        ["id"] = group.Id,
                        ["valid"] = group.IsValid,
                        ["program"] = group.Program.Program.DebugMap.Count != 0 ? group.Program.Program.DebugMap[0].SourcePath : null,
                        ["semanticHash"] = group.Program.Program.Header.CanonicalSemanticHash.HexadecimalValue,
                        ["capacity"] = group.Capacity,
                        ["count"] = group.Count,
                        ["highWaterIndex"] = group.HighWaterIndex,
                        ["tracedCount"] = group.TracedCount,
                    };
                    if (!group.IsValid || simulation.IsStepping) return info;
                    var traced = new JArray();
                    for (var index = 0; index < group.HighWaterIndex; index++)
                        if (group.TryGetAgentAt(index, out var agent) && group.IsTraced(agent))
                            traced.Add(new JObject { ["agent"] = index, ["generation"] = agent.Generation });
                    info["tracedAgents"] = traced;
                    return info;
                })),
            };
        }

        private static JObject AgentIdentity(RegisteredSimulation registered, TreeGroup group, AgentHandle agent) => new JObject
        {
            ["simulation"] = registered.Name,
            ["group"] = group.Id,
            ["agent"] = agent.Index,
            ["generation"] = agent.Generation,
            ["lastTick"] = registered.Simulation.LastTick,
        };

        private static JArray Reasons(ThinkReasons reasons)
            => new JArray(Enum.GetValues(typeof(ThinkReasons)).Cast<ThinkReasons>()
                .Where(flag => flag != ThinkReasons.None && (reasons & flag) != 0).Select(flag => flag.ToString()));

        /// <summary>Node type names of the current project registry (built-in, standard and project nodes).</summary>
        private static Func<ulong, string> TypeNames()
        {
            var registry = ProjectNodeCatalogCache.Current?.Registry;
            if (registry == null) return null;
            return id => registry.TryGet(id, out var entry) ? entry.Manifest.TypeId : null;
        }

        private static McpToolException Invalid(string message) => new McpToolException(McpAuthoringDiagnostics.MalformedArguments, message);
    }
}
