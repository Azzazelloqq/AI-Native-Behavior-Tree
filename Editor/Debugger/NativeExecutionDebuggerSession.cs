using System;
using System.Collections.Generic;

namespace AIBT.Editor.Debugger
{
    /// <summary>
    /// Attaches to an already-created, caller-owned <see cref="NativeTraceChannelOwnerV1"/> and reads
    /// it -- nothing else. This session never creates, disposes, resets, or acquires a writer lease on
    /// the channel; it only calls the channel's existing public read API
    /// (<see cref="NativeTraceChannelOwnerV1.TryGetSnapshot"/>), which itself only succeeds when the
    /// owner is <see cref="NativeOwnerStateV1.Initialized"/> (i.e. no writer job is currently leased/
    /// in-flight) -- so there is no code path here that can stall or perturb a live native pass.
    /// <para>
    /// Attach protocol: whatever owns a running native execution pass (a test harness today; a future
    /// Play-mode host once one exists -- see the P3-010 evidence's known limitations) hands this
    /// session its <see cref="NativeTraceChannelOwnerV1"/> reference directly via <see cref="Attach"/>.
    /// There is no discovery/registry mechanism, because there is nothing yet in AIBT's production
    /// code that would populate one; standalone-Player attachment is explicitly out of scope per this
    /// card.
    /// </para>
    /// </summary>
    public sealed class NativeExecutionDebuggerSession
    {
        private NativeTraceChannelOwnerV1 _owner;

        public bool IsAttached => _owner != null;

        /// <summary>Attaches to a caller-owned trace channel. Never creates or mutates it.</summary>
        public void Attach(NativeTraceChannelOwnerV1 owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// Detaches. This only clears the local reference -- the channel/owner and whatever native
        /// pass is using it are entirely untouched, so detaching mid-run cannot affect them.
        /// </summary>
        public void Detach()
        {
            _owner = null;
        }

        /// <summary>
        /// Reads the channel's current snapshot (only possible when no writer lease is active) and
        /// projects it into active-node/step-history/diagnostic views. Returns <c>false</c> if not
        /// attached or if the channel is not in a readable state (e.g. a writer is still leased) --
        /// the caller should treat that as "try again later," not an error.
        /// </summary>
        public bool TryReadTrace(out NativeDebuggerTraceView view, out NativeTraceChannelFailureV1 failure)
        {
            if (_owner == null)
            {
                view = default;
                failure = default;
                return false;
            }

            if (!_owner.TryGetSnapshot(out var snapshot, out failure))
            {
                view = default;
                return false;
            }

            view = NativeDebuggerTraceView.Build(snapshot);
            return true;
        }
    }

    /// <summary>
    /// A read-only, allocating (UI-facing, not a native hot path) projection of one
    /// <see cref="NativeTraceChannelSnapshotV1"/>: which nodes are currently active (entered without a
    /// matching exit in this snapshot), the ordered step history, and diagnostic events.
    /// </summary>
    public readonly struct NativeDebuggerTraceView
    {
        private NativeDebuggerTraceView(
            IReadOnlyList<uint> activeNodeIndices,
            IReadOnlyList<NativeTraceRecordV1> stepHistory,
            IReadOnlyList<NativeTraceRecordV1> diagnosticEvents,
            ulong droppedCount,
            bool isFaulted)
        {
            ActiveNodeIndices = activeNodeIndices;
            StepHistory = stepHistory;
            DiagnosticEvents = diagnosticEvents;
            DroppedCount = droppedCount;
            IsFaulted = isFaulted;
        }

        public IReadOnlyList<uint> ActiveNodeIndices { get; }
        public IReadOnlyList<NativeTraceRecordV1> StepHistory { get; }
        public IReadOnlyList<NativeTraceRecordV1> DiagnosticEvents { get; }
        public ulong DroppedCount { get; }
        public bool IsFaulted { get; }

        internal static NativeDebuggerTraceView Build(in NativeTraceChannelSnapshotV1 snapshot)
        {
            var stepHistory = new List<NativeTraceRecordV1>((int)snapshot.RecordCount);
            var diagnostics = new List<NativeTraceRecordV1>();
            var active = new List<uint>();

            for (var index = 0u; index < snapshot.RecordCount; index++)
            {
                var record = snapshot.Records[(int)index];
                if (record.Kind == NativeTraceEventKindV1.TraceDroppedSummary)
                {
                    continue;
                }

                stepHistory.Add(record);
                if (record.Kind == NativeTraceEventKindV1.DiagnosticRaised)
                {
                    diagnostics.Add(record);
                }
                else if (record.Kind == NativeTraceEventKindV1.NodeEntered)
                {
                    active.Add(record.RuntimeNodeIndex);
                }
                else if (record.Kind == NativeTraceEventKindV1.NodeExited)
                {
                    active.Remove(record.RuntimeNodeIndex);
                }
            }

            return new NativeDebuggerTraceView(active, stepHistory, diagnostics, snapshot.DroppedCount, snapshot.IsFaulted);
        }
    }
}
