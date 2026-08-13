namespace AIBT
{
    internal enum ReferenceParallelChildState : byte
    {
        NotStarted,
        Running,
        Success,
        Failure,
    }

    internal sealed class ReferenceParallelBranch
    {
        internal ReferenceParallelBranch(uint ordinal)
        {
            Ordinal = ordinal;
        }

        internal uint Ordinal { get; }
        internal ReferenceParallelChildState State { get; set; }
        internal System.Collections.Generic.List<ReferenceFrame> SuspendedFrames { get; set; }
    }

    internal sealed class ReferenceFrame
    {
        internal ReferenceFrame(RuntimeNodeIndex nodeIndex)
        {
            NodeIndex = nodeIndex;
            State = ReferenceFrameState.Inactive;
            SourceNodeIndex = RuntimeNodeIndex.Invalid;
        }

        internal RuntimeNodeIndex NodeIndex { get; }
        internal ReferenceFrameState State { get; set; }
        internal uint ActivationGeneration { get; set; }
        internal ulong LastTickUpdateId { get; set; }
        internal NodeStatus PendingTerminalStatus { get; set; }
        internal NodeAbortReason AbortReason { get; set; }
        internal RuntimeNodeIndex SourceNodeIndex { get; set; }
        internal bool AbortCallbackCompleted { get; set; }
        internal bool HasPendingChildResult { get; set; }
        internal NodeStatus PendingChildResult { get; set; }
        internal RuntimeNodeIndex PendingChildNodeIndex { get; set; } = RuntimeNodeIndex.Invalid;
        internal ulong LastReactiveResetUpdateId { get; set; }
        internal bool ReactiveResetPending { get; set; }
        internal bool ConfigurationDecoded { get; set; }
        internal ReferenceDecoratorKind DecoratorKind { get; set; }
        internal uint RepeaterCount { get; set; }
        internal uint RepeaterCompleted { get; set; }
        internal bool RepeaterStopOnFailure { get; set; }
        internal long DeadlineMicroseconds { get; set; }
        internal long DurationMicroseconds { get; set; }
        internal NodeStatus ConfiguredResult { get; set; }
        internal ReferenceCooldownStartPolicy CooldownStartPolicy { get; set; }
        internal bool ChildSelected { get; set; }
        internal bool CooldownDeadlinePending { get; set; }
        internal bool TimeoutTriggered { get; set; }
        internal ReferenceParallelConfiguration ParallelConfiguration { get; set; }
        internal ReferenceParallelBranch[] ParallelBranches { get; set; }
        internal uint ParallelVisitCursor { get; set; }
        internal int ActiveParallelBranchOrdinal { get; set; } = -1;
        internal bool ParallelAbortPending { get; set; }
        internal bool ParallelBranchAbortActive { get; set; }
        internal int ParallelAbortResumeTargetDepth { get; set; } = -1;
        internal NodeAbortReason ParallelAbortResumeReason { get; set; }
        internal RuntimeNodeIndex ParallelAbortResumeSource { get; set; } = RuntimeNodeIndex.Invalid;
        internal bool ParallelAbortResumeReactiveReset { get; set; }
        internal bool ParallelAbortResumeTimeout { get; set; }
    }
}
