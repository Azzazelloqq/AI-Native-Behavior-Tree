using System;
using System.Collections.Generic;
using System.Globalization;
using AIBT.Agents;
using AIBT.Authoring;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// Shows a running <see cref="BehaviorTreeAgent"/> on the graph: running nodes pulse, finished
    /// nodes flash green (success) or red (failure) and fade, and edges between running nodes form
    /// the highlighted active path. Reads live state only; never changes the agent or the layout.
    /// A <see cref="NativeTreeAgent"/> is shown from its host's activation stack: the running path
    /// pulses, nodes that leave it flash in a neutral colour (the native trace contract does not
    /// record composite exit statuses), and the root flashes with each finished run's result.
    /// </summary>
    internal sealed class GraphLiveDebugger
    {
        internal const float FlashSeconds = 0.9f;
        internal const float PulsePeriodSeconds = 0.9f;

        internal static readonly Color RunningColor = new Color(1f, 0.85f, 0.29f);
        internal static readonly Color SuccessColor = new Color(0.30f, 0.85f, 0.50f);
        internal static readonly Color FailureColor = new Color(1f, 0.36f, 0.32f);
        internal static readonly Color AbortedColor = new Color(0.62f, 0.64f, 0.70f);
        internal static readonly Color FinishedColor = new Color(0.55f, 0.78f, 1f);

        private readonly BehaviorTreeGraphView _view;
        private readonly List<(NodeId Parent, NodeId Child)> _activeEdges = new List<(NodeId, NodeId)>();
        private readonly HashSet<NodeId> _running = new HashSet<NodeId>();
        private ManagedTreeInstance _instance;
        private NodeId[] _ids = Array.Empty<NodeId>();
        private ManagedNodeState[] _last = Array.Empty<ManagedNodeState>();
        private double[] _flashStart = Array.Empty<double>();
        private Color[] _flashColor = Array.Empty<Color>();

        internal GraphLiveDebugger(BehaviorTreeGraphView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        private readonly List<uint> _activePath = new List<uint>();
        private CompiledProgram _nativeProgram;
        private bool[] _nativeRunning = Array.Empty<bool>();
        private int _nativeRuns;
        private uint _nativeRoot;

        internal BehaviorTreeAgent Agent { get; private set; }

        internal NativeTreeAgent NativeAgent { get; private set; }

        /// <summary>The attached managed or native agent component.</summary>
        internal Component Target => Agent != null ? Agent : (Component)NativeAgent;

        internal bool IsAttached => Agent != null || NativeAgent != null;

        /// <summary>Nodes currently running, as of the last <see cref="Update"/>.</summary>
        internal IReadOnlyCollection<NodeId> RunningNodes => _running;

        internal IReadOnlyList<(NodeId Parent, NodeId Child)> ActiveEdges => _activeEdges;

        /// <summary>True when the agent runs (or will run) the tree with this ID.</summary>
        internal static bool Matches(BehaviorTreeAgent agent, TreeDocument document)
        {
            if (agent == null || document == null) return false;
            var instance = agent.Instance;
            if (instance != null) return instance.Program.Document.TreeId == document.TreeId;
            return agent.Tree != null && agent.Tree.TreeId == document.TreeId.Value;
        }

        internal void Attach(BehaviorTreeAgent agent)
        {
            Detach();
            Agent = agent;
        }

        internal void Attach(NativeTreeAgent agent)
        {
            Detach();
            NativeAgent = agent;
        }

        internal void Detach()
        {
            Agent = null;
            NativeAgent = null;
            _nativeProgram = null;
            _nativeRunning = Array.Empty<bool>();
            _instance = null;
            _running.Clear();
            _activeEdges.Clear();
            _ids = Array.Empty<NodeId>();
            _view.ClearExecutionHighlight();
        }

        /// <summary>Reads the agent's node states and repaints highlights for time <paramref name="now"/> (seconds).</summary>
        internal void Update(double now)
        {
            if ((object)NativeAgent != null)
            {
                UpdateNative(now);
                return;
            }
            if (Agent == null) return;
            if (!Agent)
            {
                Detach();
                return;
            }

            var instance = Agent.Instance;
            if (!ReferenceEquals(instance, _instance)) Rebind(instance);
            _running.Clear();
            _activeEdges.Clear();
            if (_instance == null)
            {
                _view.ClearExecutionHighlight();
                return;
            }

            for (var index = 0; index < _ids.Length; index++)
            {
                var state = _instance.GetNodeState((uint)index);
                if (state != _last[index])
                {
                    if (state == ManagedNodeState.Succeeded || state == ManagedNodeState.Failed || state == ManagedNodeState.Aborted)
                    {
                        _flashStart[index] = now;
                        _flashColor[index] = state == ManagedNodeState.Succeeded ? SuccessColor
                            : state == ManagedNodeState.Failed ? FailureColor : AbortedColor;
                    }

                    _last[index] = state;
                }

                if (state == ManagedNodeState.Running) _running.Add(_ids[index]);
                if (!_view.NodesById.TryGetValue(_ids[index], out var node)) continue;
                if (state == ManagedNodeState.Running)
                {
                    node.SetHighlight(RunningColor, Pulse(now));
                }
                else
                {
                    var fade = FlashStrength(now - _flashStart[index]);
                    node.SetHighlight(_flashColor[index], fade);
                }
            }

            foreach (var key in _view.EdgesByKey.Keys)
                if (_running.Contains(key.Parent) && _running.Contains(key.Child)) _activeEdges.Add(key);
            _view.SetActiveEdges(_activeEdges);
        }

        /// <summary>Live blackboard keys of the attached agent's tree.</summary>
        internal IReadOnlyList<string> KeyNames => _instance?.KeyNames ?? (IReadOnlyList<string>)Array.Empty<string>();

        internal bool TryFormatValue(string key, out string text)
        {
            text = null;
            if (_instance == null || !_instance.TryResolveKey(key, out var slot, out _)) return false;
            if (!_instance.TryRead(slot, out var value)) return false;
            text = Format(value);
            return true;
        }

        internal string Describe()
        {
            if (NativeAgent != null)
            {
                var native = NativeAgent;
                var status = native.LastError != null ? "faulted"
                    : native.IsRunning ? "running" : native.LastResult.HasValue ? native.LastResult.Value.ToString().ToLowerInvariant() : "stopped";
                return native.name + " (native) — " + status + ", run " + (native.CompletedRuns + (native.IsRunning ? 1 : 0)).ToString(CultureInfo.InvariantCulture);
            }
            if (Agent == null) return string.Empty;
            if (_instance == null) return Agent.name + " — waiting for the agent to start";
            var state = Agent.HasFaulted ? "faulted" : _instance.LastResult.HasValue ? _instance.LastResult.Value.ToString().ToLowerInvariant() : "running";
            return Agent.name + " — " + state + ", tick " + _instance.TickCount.ToString(CultureInfo.InvariantCulture);
        }

        internal static float Pulse(double now)
            => 0.55f + 0.45f * Mathf.Sin((float)(now * 2.0 * Math.PI / PulsePeriodSeconds));

        internal static float FlashStrength(double elapsed)
            => elapsed < 0d || elapsed >= FlashSeconds ? 0f : 1f - (float)(elapsed / FlashSeconds);

        internal static string Format(BlackboardValue value)
        {
            switch (value.Type)
            {
                case BlackboardValueType.Bool: value.TryGetBool(out var flag); return flag ? "true" : "false";
                case BlackboardValueType.Int32: value.TryGetInt32(out var i); return i.ToString(CultureInfo.InvariantCulture);
                case BlackboardValueType.Int64: value.TryGetInt64(out var l); return l.ToString(CultureInfo.InvariantCulture);
                case BlackboardValueType.Float32: value.TryGetFloat32(out var f); return f.ToString("0.##", CultureInfo.InvariantCulture);
                case BlackboardValueType.Float64: value.TryGetFloat64(out var d); return d.ToString("0.##", CultureInfo.InvariantCulture);
                case BlackboardValueType.Float2:
                    value.TryGetFloat2(out var v2);
                    return "(" + N(v2.X) + ", " + N(v2.Y) + ")";
                case BlackboardValueType.Float3:
                    value.TryGetFloat3(out var v3);
                    return "(" + N(v3.X) + ", " + N(v3.Y) + ", " + N(v3.Z) + ")";
                default:
                    return value.TryGetFixedString(out var text) ? "\"" + text + "\"" : value.Type.ToString();
            }
        }

        private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private void UpdateNative(double now)
        {
            if (!NativeAgent)
            {
                Detach();
                return;
            }
            var program = NativeAgent.Program;
            if (!ReferenceEquals(program, _nativeProgram)) RebindNative(program);
            if (_nativeProgram == null)
            {
                _view.ClearExecutionHighlight();
                return;
            }

            // A host that is mid-drive (or pipelined) is not read; the last picture stays.
            var host = NativeAgent.Host;
            if (host != null && host.TryCopyActiveNodes(_activePath))
            {
                var count = _ids.Length;
                var nowRunning = new bool[count];
                foreach (var index in _activePath)
                    if (index < count) nowRunning[index] = true;
                for (var index = 0; index < count; index++)
                {
                    if (_nativeRunning[index] && !nowRunning[index])
                    {
                        _flashStart[index] = now;
                        _flashColor[index] = FinishedColor;
                    }
                }
                _nativeRunning = nowRunning;
            }
            else if (host == null)
            {
                Array.Clear(_nativeRunning, 0, _nativeRunning.Length);
            }

            if (NativeAgent.CompletedRuns != _nativeRuns)
            {
                _nativeRuns = NativeAgent.CompletedRuns;
                if (_nativeRoot < _ids.Length && NativeAgent.LastResult.HasValue)
                {
                    _flashStart[_nativeRoot] = now;
                    _flashColor[_nativeRoot] = NativeAgent.LastResult.Value == NodeStatus.Success ? SuccessColor : FailureColor;
                }
            }

            _running.Clear();
            _activeEdges.Clear();
            for (var index = 0; index < _ids.Length; index++)
            {
                if (_nativeRunning[index]) _running.Add(_ids[index]);
                if (!_view.NodesById.TryGetValue(_ids[index], out var node)) continue;
                if (_nativeRunning[index]) node.SetHighlight(RunningColor, Pulse(now));
                else node.SetHighlight(_flashColor[index], FlashStrength(now - _flashStart[index]));
            }
            foreach (var key in _view.EdgesByKey.Keys)
                if (_running.Contains(key.Parent) && _running.Contains(key.Child)) _activeEdges.Add(key);
            _view.SetActiveEdges(_activeEdges);
        }

        private void RebindNative(CompiledProgram program)
        {
            _view.ClearExecutionHighlight();
            _nativeProgram = program;
            var count = program?.Nodes.Count ?? 0;
            _ids = new NodeId[count];
            _nativeRunning = new bool[count];
            _flashStart = new double[count];
            _flashColor = new Color[count];
            _last = new ManagedNodeState[count];
            for (var index = 0; index < count; index++) _flashStart[index] = double.NegativeInfinity;
            if (program == null) return;
            foreach (var entry in program.DebugMap)
                if (entry.RuntimeNodeIndex < count) _ids[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            _nativeRoot = program.Header.RootNodeIndex;
            _nativeRuns = NativeAgent.CompletedRuns;
        }

        private void Rebind(ManagedTreeInstance instance)
        {
            _view.ClearExecutionHighlight();
            _instance = instance;
            var count = instance?.NodeIds.Length ?? 0;
            _ids = new NodeId[count];
            _last = new ManagedNodeState[count];
            _flashStart = new double[count];
            _flashColor = new Color[count];
            for (var index = 0; index < count; index++)
            {
                _ids[index] = new NodeId(instance.NodeIds[index]);
                _flashStart[index] = double.NegativeInfinity;
            }
        }
    }
}
