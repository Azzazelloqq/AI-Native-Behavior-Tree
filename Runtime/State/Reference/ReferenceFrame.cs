namespace AIBT
{
    /// <summary>
    /// How an update continues after a retaining composite's takeover abort (ADR-P10-013): the
    /// same way it would have continued at the point where the guard branch came to rest.
    /// </summary>
    internal enum ReferenceRetainedRest : byte
    {
        /// <summary>A leaf returned Running in this tick.</summary>
        LeafTick,
        /// <summary>A parallel visit ended non-terminal, or an already ticked leaf was reached again.</summary>
        Visit,
    }

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

        /// <summary>
        /// Moves <paramref name="frames"/>[<paramref name="start"/>..] into this branch's suspended
        /// frames, reusing the list this branch suspended into before (P10-001).
        /// </summary>
        internal void Suspend(System.Collections.Generic.List<ReferenceFrame> frames, int start)
        {
            var storage = _storage ?? (_storage = new System.Collections.Generic.List<ReferenceFrame>());
            storage.Clear();
            for (var index = start; index < frames.Count; index++) storage.Add(frames[index]);
            frames.RemoveRange(start, frames.Count - start);
            SuspendedFrames = storage;
        }

        internal void Reset()
        {
            State = ReferenceParallelChildState.NotStarted;
            SuspendedFrames = null;
            _storage?.Clear();
        }

        private System.Collections.Generic.List<ReferenceFrame> _storage;
    }

    /// <summary>
    /// One node activation on the reference machine's execution stack. A node is on the stack at
    /// most once, so the machine keeps one frame object per node and calls <see cref="Reset"/> on
    /// every activation instead of allocating (P10-001). <see cref="Reset"/> must leave the frame
    /// exactly as the constructor does.
    /// </summary>
    internal sealed class ReferenceFrame
    {
        private ReferenceParallelBranch[] _parallelBranchCache;

        internal ReferenceFrame(RuntimeNodeIndex nodeIndex)
        {
            NodeIndex = nodeIndex;
            State = ReferenceFrameState.Inactive;
            SourceNodeIndex = RuntimeNodeIndex.Invalid;
        }

        /// <summary>Returns this frame to the state of a newly constructed frame for <paramref name="nodeIndex"/>.</summary>
        internal void Reset(RuntimeNodeIndex nodeIndex)
        {
            if (ParallelBranches != null)
            {
                for (var index = 0; index < ParallelBranches.Length; index++) ParallelBranches[index].Reset();
                _parallelBranchCache = ParallelBranches;
            }

            NodeIndex = nodeIndex;
            State = ReferenceFrameState.Inactive;
            ActivationGeneration = 0;
            LastTickUpdateId = 0;
            PendingTerminalStatus = default;
            AbortReason = default;
            SourceNodeIndex = RuntimeNodeIndex.Invalid;
            AbortCallbackCompleted = false;
            HasPendingChildResult = false;
            PendingChildResult = default;
            PendingChildNodeIndex = RuntimeNodeIndex.Invalid;
            LastReactiveResetUpdateId = 0;
            ReactiveResetPending = false;
            ConfigurationDecoded = false;
            DecoratorKind = default;
            RepeaterCount = 0;
            RepeaterCompleted = 0;
            RepeaterStopOnFailure = false;
            DeadlineMicroseconds = 0;
            DurationMicroseconds = 0;
            ConfiguredResult = default;
            CooldownStartPolicy = default;
            ChildSelected = false;
            CooldownDeadlinePending = false;
            TimeoutTriggered = false;
            ParallelConfiguration = default;
            ParallelBranches = null;
            ParallelVisitCursor = 0;
            ActiveParallelBranchOrdinal = -1;
            ParallelAbortPending = false;
            ParallelBranchAbortActive = false;
            ParallelAbortResumeTargetDepth = -1;
            ParallelAbortResumeReason = default;
            ParallelAbortResumeSource = RuntimeNodeIndex.Invalid;
            ParallelAbortResumeReactiveReset = false;
            ParallelAbortResumeTimeout = false;
            _retainedFrames?.Clear();
            _heldGuardFrames?.Clear();
            RetainedChildOrdinal = 0;
            LastGuardPassUpdateId = 0;
            GuardPassPending = false;
            GuardPassActive = false;
            RetainedAbortActive = false;
            RetainedEndPending = false;
            RetainedEndStatus = default;
            TakeoverRest = default;
        }

        private System.Collections.Generic.List<ReferenceFrame> _retainedFrames;
        private System.Collections.Generic.List<ReferenceFrame> _heldGuardFrames;

        /// <summary>
        /// Retaining reactive composite (ADR-P10-013): the retained branch, parked off the execution
        /// stack while the guard pass runs. Empty whenever no guard pass holds a branch.
        /// </summary>
        internal System.Collections.Generic.List<ReferenceFrame> RetainedFrames
            => _retainedFrames ?? (_retainedFrames = new System.Collections.Generic.List<ReferenceFrame>());

        /// <summary>
        /// Retaining reactive composite: the guard branch that took over, held off the stack while
        /// the retained branch below it is aborted. Empty otherwise.
        /// </summary>
        internal System.Collections.Generic.List<ReferenceFrame> HeldGuardFrames
            => _heldGuardFrames ?? (_heldGuardFrames = new System.Collections.Generic.List<ReferenceFrame>());

        internal bool HasParkedFrames
            => (_retainedFrames != null && _retainedFrames.Count != 0)
                || (_heldGuardFrames != null && _heldGuardFrames.Count != 0);

        internal uint RetainedChildOrdinal { get; set; }
        internal ulong LastGuardPassUpdateId { get; set; }
        internal bool GuardPassPending { get; set; }
        internal bool GuardPassActive { get; set; }
        internal bool RetainedAbortActive { get; set; }
        internal bool RetainedEndPending { get; set; }
        internal NodeStatus RetainedEndStatus { get; set; }
        internal ReferenceRetainedRest TakeoverRest { get; set; }

        /// <summary>
        /// Branch records for a new parallel activation with <paramref name="count"/> children, all
        /// <see cref="ReferenceParallelChildState.NotStarted"/>; reuses the previous activation's.
        /// </summary>
        internal ReferenceParallelBranch[] CreateParallelBranches(uint count)
        {
            var cached = _parallelBranchCache;
            _parallelBranchCache = null;
            if (cached != null && cached.Length == count) return cached;
            var branches = new ReferenceParallelBranch[count];
            for (uint ordinal = 0; ordinal < count; ordinal++) branches[ordinal] = new ReferenceParallelBranch(ordinal);
            return branches;
        }

        internal RuntimeNodeIndex NodeIndex { get; private set; }
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
