using System.Collections.Generic;
using AIBT.Editor.Debugger;

namespace AIBT.Editor.Trace
{
    /// <summary>One trace event plus the active-node set immediately after it was applied.</summary>
    public readonly struct TraceStepEntry
    {
        internal TraceStepEntry(int index, NativeTraceRecordV1 record, IReadOnlyList<uint> activeRuntimeNodeIndicesAfter)
        {
            Index = index;
            Record = record;
            ActiveRuntimeNodeIndicesAfter = activeRuntimeNodeIndicesAfter;
        }

        public int Index { get; }
        public NativeTraceRecordV1 Record { get; }

        /// <summary>Runtime node indices considered active (entered, not yet exited) as of this step, replayed from the start of the snapshot.</summary>
        public IReadOnlyList<uint> ActiveRuntimeNodeIndicesAfter { get; }
    }

    /// <summary>A diagnostic event correlated to the step and graph state that produced it.</summary>
    public readonly struct DiagnosticCorrelation
    {
        internal DiagnosticCorrelation(NativeTraceRecordV1 record, int stepIndex, IReadOnlyList<uint> activeRuntimeNodeIndicesAtStep)
        {
            Record = record;
            StepIndex = stepIndex;
            ActiveRuntimeNodeIndicesAtStep = activeRuntimeNodeIndicesAtStep;
        }

        public NativeTraceRecordV1 Record { get; }
        public int StepIndex { get; }
        public IReadOnlyList<uint> ActiveRuntimeNodeIndicesAtStep { get; }
    }

    /// <summary>
    /// A scrubbable timeline over one <see cref="NativeDebuggerTraceView"/> snapshot (P3-010's
    /// read-only channel view -- this type never reads the channel itself, only replays a view
    /// already produced by <c>NativeExecutionDebuggerSession.TryReadTrace</c>, per this card's
    /// "pure consumer" scope). Replay is a pure function of the ordered step history: walking
    /// NodeEntered/NodeExited events forward and recording the active set after each step, so
    /// scrubbing to step N reproduces exactly the active-node state that step actually produced.
    /// </summary>
    public sealed class TraceTimelineModel
    {
        private TraceTimelineModel(
            IReadOnlyList<TraceStepEntry> steps,
            IReadOnlyList<DiagnosticCorrelation> diagnostics,
            ulong droppedCount,
            bool isFaulted)
        {
            Steps = steps;
            Diagnostics = diagnostics;
            DroppedCount = droppedCount;
            IsFaulted = isFaulted;
        }

        public IReadOnlyList<TraceStepEntry> Steps { get; }
        public IReadOnlyList<DiagnosticCorrelation> Diagnostics { get; }

        /// <summary>Events the bounded channel dropped or overwrote before this snapshot was read.</summary>
        public ulong DroppedCount { get; }
        public bool IsFaulted { get; }

        /// <summary>
        /// True when the view is not a complete history -- the caller must show this explicitly
        /// ("channel full / events dropped") rather than presenting a truncated trace as complete.
        /// </summary>
        public bool HasDroppedEvents => DroppedCount > 0;

        public IReadOnlyList<uint> ActiveRuntimeNodeIndicesAtStep(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= Steps.Count)
            {
                return System.Array.Empty<uint>();
            }

            return Steps[stepIndex].ActiveRuntimeNodeIndicesAfter;
        }

        /// <summary>An empty timeline -- used before any trace has ever been read.</summary>
        public static TraceTimelineModel Empty { get; } =
            new TraceTimelineModel(System.Array.Empty<TraceStepEntry>(), System.Array.Empty<DiagnosticCorrelation>(), 0, false);

        public static TraceTimelineModel Build(NativeDebuggerTraceView view)
        {
            if (view.StepHistory == null)
            {
                return Empty;
            }

            var steps = new List<TraceStepEntry>(view.StepHistory.Count);
            var diagnostics = new List<DiagnosticCorrelation>(view.DiagnosticEvents?.Count ?? 0);
            var active = new List<uint>();

            for (var index = 0; index < view.StepHistory.Count; index++)
            {
                var record = view.StepHistory[index];
                if (record.Kind == NativeTraceEventKindV1.NodeEntered)
                {
                    active.Add(record.RuntimeNodeIndex);
                }
                else if (record.Kind == NativeTraceEventKindV1.NodeExited)
                {
                    active.Remove(record.RuntimeNodeIndex);
                }

                var activeSnapshot = active.ToArray();
                steps.Add(new TraceStepEntry(index, record, activeSnapshot));

                if (record.Kind == NativeTraceEventKindV1.DiagnosticRaised)
                {
                    diagnostics.Add(new DiagnosticCorrelation(record, index, activeSnapshot));
                }
            }

            return new TraceTimelineModel(steps, diagnostics, view.DroppedCount, view.IsFaulted);
        }
    }
}
