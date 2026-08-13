using System;

namespace AIBT
{
    internal enum ReferenceTraceEventKind : byte
    {
        UpdateStarted,
        UpdateCompleted,
        NodeEntered,
        NodeTicked,
        NodeAbortStarted,
        NodeExited,
        CommandEmitted,
        CompletionConsumed,
        CompletionDiscarded,
        BlackboardChanged,
        ObserverQueued,
        ObserverEvaluated,
        DiagnosticRaised,
        BudgetYielded,
        ExecutionResumed,
    }

    internal readonly struct ReferenceTraceRecord
    {
        internal const uint FormatVersion = 1;

        internal ReferenceTraceRecord(
            ulong updateId,
            Revision snapshotRevision,
            CompiledHash treeSemanticHash,
            TreeInstanceId treeInstanceId,
            ulong sequence,
            ReferenceTraceEventKind kind,
            RuntimeNodeIndex nodeIndex = default,
            NodeStatus? status = null,
            NodeExitReason? exitReason = null,
            NodeAbortReason? abortReason = null,
            RuntimeNodeIndex sourceNodeIndex = default,
            DiagnosticCode diagnosticCode = default,
            ulong stableBlackboardKeyId = 0,
            ulong oldBlackboardVersion = 0,
            ulong newBlackboardVersion = 0)
        {
            if (updateId == 0) throw new ArgumentOutOfRangeException(nameof(updateId));
            if (!treeSemanticHash.IsValid) throw new ArgumentException("A semantic hash is required.", nameof(treeSemanticHash));
            if (!treeInstanceId.IsValid) throw new ArgumentException("A tree instance ID is required.", nameof(treeInstanceId));
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));

            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            TreeSemanticHash = treeSemanticHash;
            TreeInstanceId = treeInstanceId;
            Sequence = sequence;
            Kind = kind;
            NodeIndex = nodeIndex;
            Status = status;
            ExitReason = exitReason;
            AbortReason = abortReason;
            SourceNodeIndex = sourceNodeIndex;
            DiagnosticCode = diagnosticCode;
            StableBlackboardKeyId = stableBlackboardKeyId;
            OldBlackboardVersion = oldBlackboardVersion;
            NewBlackboardVersion = newBlackboardVersion;
        }

        internal uint TraceFormatVersion => FormatVersion;
        internal ulong UpdateId { get; }
        internal Revision SnapshotRevision { get; }
        internal CompiledHash TreeSemanticHash { get; }
        internal TreeInstanceId TreeInstanceId { get; }
        internal ulong Sequence { get; }
        internal ReferenceTraceEventKind Kind { get; }
        internal RuntimeNodeIndex NodeIndex { get; }
        internal NodeStatus? Status { get; }
        internal NodeExitReason? ExitReason { get; }
        internal NodeAbortReason? AbortReason { get; }
        internal RuntimeNodeIndex SourceNodeIndex { get; }
        internal DiagnosticCode DiagnosticCode { get; }
        internal ulong StableBlackboardKeyId { get; }
        internal ulong OldBlackboardVersion { get; }
        internal ulong NewBlackboardVersion { get; }
    }

    internal interface IReferenceTraceSink
    {
        void Record(in ReferenceTraceRecord record);
    }

    internal sealed class NullReferenceTraceSink : IReferenceTraceSink
    {
        internal static readonly NullReferenceTraceSink Instance = new NullReferenceTraceSink();

        private NullReferenceTraceSink()
        {
        }

        public void Record(in ReferenceTraceRecord record)
        {
        }
    }
}
