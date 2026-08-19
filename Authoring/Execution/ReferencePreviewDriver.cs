using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    /// <summary>
    /// Drives the accepted Phase 1 managed reference executor (<c>ReferenceExecutionMachine</c>,
    /// internal to <c>AIBT.Runtime</c>) for editor preview/stepping, so in-editor stepping semantics
    /// cannot drift from the accepted headless oracle. This type owns no execution semantics of its
    /// own: every state transition is a direct call into the existing machine's public
    /// <c>BeginUpdate</c>/<c>AdvanceOneStep</c>/<c>Restart</c> API; this class only translates its
    /// internal contracts to a public shape that <c>AIBT.Editor</c> (which has no internals
    /// visibility into <c>AIBT.Runtime</c>) can consume.
    /// <para>
    /// The executable node-behavior set is fixed to the already-shipped Phase 1 fixture/built-in
    /// registries (<c>ReferenceLeafRegistry.CreatePhase1Fixtures()</c> and the sibling
    /// <c>CreatePhase1BuiltIns()</c> composite/decorator/parallel registries already used by the
    /// headless behavior-case runner). AIBT ships no production per-project leaf-behavior
    /// registration mechanism yet, so only trees built from built-in composites/decorators and the
    /// <c>aibt.test.success</c>/<c>aibt.test.failure</c>/<c>aibt.test.running</c> fixture leaves can
    /// be previewed; this is a known limitation, not a silent weakening (see the P3-009 evidence).
    /// </para>
    /// </summary>
    public sealed class ReferencePreviewDriver
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 1);

        private readonly ReferenceExecutionMachine _machine;
        private readonly PreviewTraceSink _traceSink;
        private readonly IReadOnlyDictionary<uint, NodeId> _nodeIdByRuntimeIndex;
        private readonly IReadOnlyDictionary<ulong, string> _blackboardKeyNames;
        private ulong _updateId;
        private bool _hasOpenTick;

        private ReferencePreviewDriver(
            ReferenceExecutionMachine machine,
            PreviewTraceSink traceSink,
            IReadOnlyDictionary<uint, NodeId> nodeIdByRuntimeIndex,
            IReadOnlyDictionary<ulong, string> blackboardKeyNames)
        {
            _machine = machine;
            _traceSink = traceSink;
            _nodeIdByRuntimeIndex = nodeIdByRuntimeIndex;
            _blackboardKeyNames = blackboardKeyNames;
        }

        /// <summary>
        /// Compiles <paramref name="document"/> against the fixed Phase 1 fixture/built-in node set
        /// and constructs a driver ready to step it. Returns <c>false</c> (with no driver) if the
        /// document does not compile against that set -- the caller is expected to surface
        /// <paramref name="diagnostics"/>, not to treat this as an exceptional/crashing condition.
        /// </summary>
        public static bool TryCreate(
            TreeDocument document,
            string sourceId,
            out ReferencePreviewDriver driver,
            out DiagnosticCollection diagnostics)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("A source ID is required.", nameof(sourceId));

            var registry = ReferencePreviewFixtureEnvironment.CreateNodeRegistry();
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, CompilerVersion);
            var compilation = ReferenceCompiler.Compile(document, registry, options);
            if (!compilation.Success)
            {
                driver = null;
                diagnostics = compilation.Diagnostics;
                return false;
            }

            var nodeIdByRuntimeIndex = new Dictionary<uint, NodeId>();
            foreach (var entry in compilation.Program.DebugMap)
            {
                nodeIdByRuntimeIndex[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            }

            var blackboardKeyNames = new Dictionary<ulong, string>();
            foreach (var key in document.Blackboard)
            {
                blackboardKeyNames[StableHash.Fnv1A64(key.Id)] = key.Id;
            }

            var traceSink = new PreviewTraceSink();
            var machine = new ReferenceExecutionMachine(
                compilation.Program,
                new TreeInstanceId(1),
                ReferencePreviewFixtureEnvironment.CreateLeafRegistry(),
                traceSink,
                ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                RegisteredBlackboardRegistry.Empty,
                ReferencePreviewFixtureEnvironment.CreateObserverRegistry());

            driver = new ReferencePreviewDriver(machine, traceSink, nodeIdByRuntimeIndex, blackboardKeyNames);
            diagnostics = compilation.Diagnostics;
            return true;
        }

        /// <summary>
        /// The fixed node registry <see cref="TryCreate"/> compiles against (built-ins plus the
        /// Phase 1 fixture leaves) -- exposed so editor UI can render the same tree with the same
        /// node-kind metadata (e.g. via <c>BehaviorTreeGraphView</c>) without a second, potentially
        /// divergent registry-construction path.
        /// </summary>
        public static NodeRegistry CreatePreviewNodeRegistry() => ReferencePreviewFixtureEnvironment.CreateNodeRegistry();

        /// <summary>The terminal root status, if the tree instance has reached one.</summary>
        public NodeStatus? TerminalResult => _machine.TerminalResult;

        /// <summary>Whether a host update ("tick") is open and mid-flight (paused between atomic steps).</summary>
        public bool HasOpenTick => _hasOpenTick;

        /// <summary>Node identities the reference executor currently considers active (entered, not yet exited).</summary>
        public IReadOnlyList<NodeId> ActiveNodeIds
        {
            get
            {
                var result = new List<NodeId>(_traceSink.ActiveRuntimeIndices.Count);
                foreach (var runtimeIndex in _traceSink.ActiveRuntimeIndices)
                {
                    if (_nodeIdByRuntimeIndex.TryGetValue(runtimeIndex, out var nodeId))
                    {
                        result.Add(nodeId);
                    }
                }

                return result;
            }
        }

        /// <summary>Captures blackboard/operation state at the current stable API boundary.</summary>
        public ReferencePreviewInspection CaptureInspection()
        {
            var inspection = _machine.CaptureInspection();
            var values = new List<ReferencePreviewBlackboardValue>(inspection.Blackboard.Entries.Count);
            foreach (var entry in inspection.Blackboard.Entries)
            {
                _blackboardKeyNames.TryGetValue(entry.StableKeyId, out var name);
                values.Add(new ReferencePreviewBlackboardValue(
                    name ?? entry.StableKeyId.ToString(),
                    entry.Version,
                    entry.IsRegistered,
                    entry.IsRegistered ? default : entry.BuiltInValue,
                    entry.IsRegistered ? entry.Type.TypeId : 0,
                    entry.IsRegistered ? entry.Type.Version : 0));
            }

            return new ReferencePreviewInspection(ActiveNodeIds, inspection.ActiveOperationCount, values);
        }

        /// <summary>
        /// Opens a new host update ("tick"). Must be called before <see cref="StepAtomic"/> when no
        /// tick is currently open (<see cref="HasOpenTick"/> is <c>false</c>).
        /// </summary>
        public ReferencePreviewEnvelope BeginTick(long timeMicroseconds = 0)
        {
            if (_hasOpenTick)
            {
                throw new InvalidOperationException("A tick is already open; step or run it to a boundary first.");
            }

            _updateId++;
            var context = new ReferenceUpdateContext(_updateId, new Revision(_updateId), timeMicroseconds);
            var envelope = _machine.BeginUpdate(context);
            return Deliver(envelope);
        }

        /// <summary>
        /// Advances exactly one atomic machine step (Enter, one Tick, one Abort transition, Exit, or
        /// one queued-observer evaluation) within the currently open tick -- the same primitive the
        /// headless reference executor uses for a single discrete step.
        /// </summary>
        public ReferencePreviewEnvelope StepAtomic()
        {
            if (!_hasOpenTick)
            {
                throw new InvalidOperationException("No tick is open; call BeginTick first.");
            }

            return Deliver(_machine.AdvanceOneStep());
        }

        /// <summary>
        /// Opens a tick if needed, then advances atomic steps until the tick reaches a boundary
        /// (Completed/Waiting/Rejected/Faulted) or a node in <paramref name="breakpoints"/> is
        /// entered, in which case the tick is left open for a later call to resume it. Calling this
        /// again after a breakpoint pause continues from exactly where it stopped.
        /// </summary>
        public ReferencePreviewEnvelope RunTick(ISet<NodeId> breakpoints = null, long timeMicroseconds = 0)
        {
            var aggregatedTrace = new List<ReferencePreviewTraceEvent>();
            ReferencePreviewEnvelope last;
            if (!_hasOpenTick)
            {
                last = BeginTick(timeMicroseconds);
                aggregatedTrace.AddRange(last.TraceEvents);
                if (!_hasOpenTick)
                {
                    return last;
                }
            }
            else
            {
                last = default;
            }

            while (_hasOpenTick)
            {
                var step = StepAtomic();
                aggregatedTrace.AddRange(step.TraceEvents);
                last = step;
                if (!_hasOpenTick || (breakpoints != null && HitsBreakpoint(step, breakpoints)))
                {
                    break;
                }
            }

            return new ReferencePreviewEnvelope(last.Progress, last.RootResult, last.Steps, aggregatedTrace);
        }

        /// <summary>Restarts the tree instance for a fresh preview run of the same compiled program.</summary>
        public ReferencePreviewEnvelope Restart()
        {
            var envelope = Deliver(_machine.Restart());
            _traceSink.ResetActive();
            return envelope;
        }

        private static bool HitsBreakpoint(ReferencePreviewEnvelope step, ISet<NodeId> breakpoints)
        {
            foreach (var traceEvent in step.TraceEvents)
            {
                if (traceEvent.Kind == ReferencePreviewTraceEventKind.NodeEntered
                    && traceEvent.Node.HasValue
                    && breakpoints.Contains(traceEvent.Node.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private ReferencePreviewEnvelope Deliver(ReferenceExecutionEnvelope envelope)
        {
            var events = _traceSink.Take(_nodeIdByRuntimeIndex);
            _hasOpenTick = envelope.Progress == ReferenceExecutionProgress.Suspended;
            return new ReferencePreviewEnvelope(Map(envelope.Progress), envelope.RootResult, envelope.Steps, events);
        }

        private static ReferencePreviewProgress Map(ReferenceExecutionProgress value)
        {
            switch (value)
            {
                case ReferenceExecutionProgress.Completed: return ReferencePreviewProgress.Completed;
                case ReferenceExecutionProgress.Waiting: return ReferencePreviewProgress.Waiting;
                case ReferenceExecutionProgress.Suspended: return ReferencePreviewProgress.Suspended;
                case ReferenceExecutionProgress.Rejected: return ReferencePreviewProgress.Rejected;
                case ReferenceExecutionProgress.Faulted: return ReferencePreviewProgress.Faulted;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private sealed class PreviewTraceSink : IReferenceTraceSink
        {
            private readonly List<ReferenceTraceRecord> _records = new List<ReferenceTraceRecord>();
            private readonly HashSet<uint> _activeRuntimeIndices = new HashSet<uint>();

            internal IReadOnlyCollection<uint> ActiveRuntimeIndices => _activeRuntimeIndices;

            public void Record(in ReferenceTraceRecord record)
            {
                _records.Add(record);
                if (record.Kind == ReferenceTraceEventKind.NodeEntered && record.NodeIndex.IsValid)
                {
                    _activeRuntimeIndices.Add(record.NodeIndex.Value);
                }
                else if (record.Kind == ReferenceTraceEventKind.NodeExited && record.NodeIndex.IsValid)
                {
                    _activeRuntimeIndices.Remove(record.NodeIndex.Value);
                }
            }

            internal void ResetActive() => _activeRuntimeIndices.Clear();

            internal List<ReferencePreviewTraceEvent> Take(IReadOnlyDictionary<uint, NodeId> nodeIdByRuntimeIndex)
            {
                var result = new List<ReferencePreviewTraceEvent>(_records.Count);
                foreach (var record in _records)
                {
                    NodeId? node = null;
                    if (record.NodeIndex.IsValid && nodeIdByRuntimeIndex.TryGetValue(record.NodeIndex.Value, out var mapped))
                    {
                        node = mapped;
                    }

                    NodeId? sourceNode = null;
                    if (record.SourceNodeIndex.IsValid && nodeIdByRuntimeIndex.TryGetValue(record.SourceNodeIndex.Value, out var mappedSource))
                    {
                        sourceNode = mappedSource;
                    }

                    result.Add(new ReferencePreviewTraceEvent(record.Sequence, Map(record.Kind), node, record.Status, sourceNode));
                }

                _records.Clear();
                return result;
            }

            private static ReferencePreviewTraceEventKind Map(ReferenceTraceEventKind value)
            {
                switch (value)
                {
                    case ReferenceTraceEventKind.UpdateStarted: return ReferencePreviewTraceEventKind.UpdateStarted;
                    case ReferenceTraceEventKind.UpdateCompleted: return ReferencePreviewTraceEventKind.UpdateCompleted;
                    case ReferenceTraceEventKind.NodeEntered: return ReferencePreviewTraceEventKind.NodeEntered;
                    case ReferenceTraceEventKind.NodeTicked: return ReferencePreviewTraceEventKind.NodeTicked;
                    case ReferenceTraceEventKind.NodeAbortStarted: return ReferencePreviewTraceEventKind.NodeAbortStarted;
                    case ReferenceTraceEventKind.NodeExited: return ReferencePreviewTraceEventKind.NodeExited;
                    case ReferenceTraceEventKind.CommandEmitted: return ReferencePreviewTraceEventKind.CommandEmitted;
                    case ReferenceTraceEventKind.CompletionConsumed: return ReferencePreviewTraceEventKind.CompletionConsumed;
                    case ReferenceTraceEventKind.CompletionDiscarded: return ReferencePreviewTraceEventKind.CompletionDiscarded;
                    case ReferenceTraceEventKind.BlackboardChanged: return ReferencePreviewTraceEventKind.BlackboardChanged;
                    case ReferenceTraceEventKind.ObserverQueued: return ReferencePreviewTraceEventKind.ObserverQueued;
                    case ReferenceTraceEventKind.ObserverEvaluated: return ReferencePreviewTraceEventKind.ObserverEvaluated;
                    case ReferenceTraceEventKind.DiagnosticRaised: return ReferencePreviewTraceEventKind.DiagnosticRaised;
                    case ReferenceTraceEventKind.BudgetYielded: return ReferencePreviewTraceEventKind.BudgetYielded;
                    case ReferenceTraceEventKind.ExecutionResumed: return ReferencePreviewTraceEventKind.ExecutionResumed;
                    default: throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }
    }
}
