using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    /// <summary>Mirrors <c>ReferenceExecutionProgress</c> (internal, Runtime) across the assembly boundary.</summary>
    public enum ReferencePreviewProgress
    {
        Completed,
        Waiting,
        Suspended,
        Rejected,
        Faulted,
    }

    /// <summary>Mirrors <c>ReferenceTraceEventKind</c> (internal, Runtime) across the assembly boundary.</summary>
    public enum ReferencePreviewTraceEventKind
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

    /// <summary>
    /// One atomic reference-executor trace event, translated from the internal
    /// <c>ReferenceTraceRecord</c> into the public authoring node identity space so editor code
    /// (which has no internals visibility into <c>AIBT.Runtime</c>) can consume it.
    /// </summary>
    public readonly struct ReferencePreviewTraceEvent
    {
        public ReferencePreviewTraceEvent(
            ulong sequence,
            ReferencePreviewTraceEventKind kind,
            NodeId? node,
            NodeStatus? status,
            NodeId? sourceNode)
        {
            Sequence = sequence;
            Kind = kind;
            Node = node;
            Status = status;
            SourceNode = sourceNode;
        }

        public ulong Sequence { get; }
        public ReferencePreviewTraceEventKind Kind { get; }
        public NodeId? Node { get; }
        public NodeStatus? Status { get; }
        public NodeId? SourceNode { get; }
    }

    /// <summary>One blackboard slot as observed at an inspection boundary.</summary>
    public readonly struct ReferencePreviewBlackboardValue
    {
        public ReferencePreviewBlackboardValue(
            string key,
            ulong version,
            bool isRegistered,
            BlackboardValue builtInValue,
            ulong registeredTypeId,
            uint registeredTypeVersion)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version;
            IsRegistered = isRegistered;
            BuiltInValue = builtInValue;
            RegisteredTypeId = registeredTypeId;
            RegisteredTypeVersion = registeredTypeVersion;
        }

        public string Key { get; }
        public ulong Version { get; }
        public bool IsRegistered { get; }

        /// <summary>Valid only when <see cref="IsRegistered"/> is <c>false</c>.</summary>
        public BlackboardValue BuiltInValue { get; }

        /// <summary>Valid only when <see cref="IsRegistered"/> is <c>true</c>; registered payload bytes are not exposed.</summary>
        public ulong RegisteredTypeId { get; }
        public uint RegisteredTypeVersion { get; }
    }

    /// <summary>
    /// A stable-boundary snapshot of execution state, mirroring the internal
    /// <c>ReferenceExecutionInspection</c> plus the active node identities the internal type omits.
    /// </summary>
    public readonly struct ReferencePreviewInspection
    {
        public ReferencePreviewInspection(
            IReadOnlyList<NodeId> activeNodeIds,
            uint activeOperationCount,
            IReadOnlyList<ReferencePreviewBlackboardValue> blackboard)
        {
            ActiveNodeIds = activeNodeIds ?? throw new ArgumentNullException(nameof(activeNodeIds));
            ActiveOperationCount = activeOperationCount;
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        public IReadOnlyList<NodeId> ActiveNodeIds { get; }
        public uint ActiveOperationCount { get; }
        public IReadOnlyList<ReferencePreviewBlackboardValue> Blackboard { get; }
    }

    /// <summary>Mirrors <c>ReferenceExecutionEnvelope</c> (internal, Runtime) for a single driver call.</summary>
    public readonly struct ReferencePreviewEnvelope
    {
        public ReferencePreviewEnvelope(
            ReferencePreviewProgress progress,
            NodeStatus? rootResult,
            ulong steps,
            IReadOnlyList<ReferencePreviewTraceEvent> traceEvents)
        {
            Progress = progress;
            RootResult = rootResult;
            Steps = steps;
            TraceEvents = traceEvents ?? throw new ArgumentNullException(nameof(traceEvents));
        }

        public ReferencePreviewProgress Progress { get; }
        public NodeStatus? RootResult { get; }
        public ulong Steps { get; }
        public IReadOnlyList<ReferencePreviewTraceEvent> TraceEvents { get; }
    }
}
