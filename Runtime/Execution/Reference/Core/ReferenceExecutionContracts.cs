using System;

namespace AIBT
{
    internal readonly struct ReferenceExecutionInspection
    {
        internal ReferenceExecutionInspection(
            ReferenceBlackboardSnapshot blackboard,
            uint activeNodeCount,
            uint activeOperationCount)
        {
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            ActiveNodeCount = activeNodeCount;
            ActiveOperationCount = activeOperationCount;
        }

        internal ReferenceBlackboardSnapshot Blackboard { get; }
        internal uint ActiveNodeCount { get; }
        internal uint ActiveOperationCount { get; }
    }

    internal readonly struct ReferenceStepBudget : IEquatable<ReferenceStepBudget>
    {
        private ReferenceStepBudget(bool isUnlimited, ulong stepLimit)
        {
            IsUnlimited = isUnlimited;
            StepLimit = stepLimit;
        }

        internal static ReferenceStepBudget Unlimited { get; }
            = new ReferenceStepBudget(true, 0);

        internal static ReferenceStepBudget Limited(ulong stepLimit)
            => new ReferenceStepBudget(false, stepLimit);

        internal bool IsUnlimited { get; }
        internal ulong StepLimit { get; }

        public bool Equals(ReferenceStepBudget other)
            => IsUnlimited == other.IsUnlimited && StepLimit == other.StepLimit;

        public override bool Equals(object obj)
            => obj is ReferenceStepBudget other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (IsUnlimited.GetHashCode() * 397) ^ StepLimit.GetHashCode(); }
        }
    }

    internal enum ReferenceExecutionProgress : byte
    {
        Completed,
        Waiting,
        Suspended,
        Rejected,
        Faulted,
    }

    internal enum ReferenceFrameState : byte
    {
        Inactive,
        Entered,
        Running,
        TerminalPendingExit,
        Aborting,
    }

    internal readonly struct ReferenceUpdateContext
    {
        internal ReferenceUpdateContext(
            ulong updateId,
            Revision snapshotRevision,
            long timeMicroseconds,
            CompletionBatch completions = null)
        {
            if (updateId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(updateId));
            }

            if (!snapshotRevision.IsValid)
            {
                throw new ArgumentException("A snapshot revision is required.", nameof(snapshotRevision));
            }

            UpdateId = updateId;
            SnapshotRevision = snapshotRevision;
            TimeMicroseconds = timeMicroseconds;
            Completions = completions ?? CompletionBatch.Empty;
        }

        internal ulong UpdateId { get; }

        internal Revision SnapshotRevision { get; }

        internal long TimeMicroseconds { get; }
        internal CompletionBatch Completions { get; }
    }

    internal readonly struct ReferenceExecutionEnvelope
    {
        internal ReferenceExecutionEnvelope(
            ReferenceExecutionProgress progress,
            NodeStatus? rootResult,
            ulong segmentSteps,
            ulong cumulativeSteps,
            DiagnosticCollection diagnostics,
            CommandBatch commands = null)
        {
            Progress = progress;
            RootResult = rootResult;
            SegmentSteps = segmentSteps;
            CumulativeSteps = cumulativeSteps;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Commands = commands ?? CommandBatch.Empty;
        }

        internal ReferenceExecutionProgress Progress { get; }

        internal NodeStatus? RootResult { get; }

        internal ulong SegmentSteps { get; }

        internal ulong CumulativeSteps { get; }

        internal ulong Steps => SegmentSteps;

        internal DiagnosticCollection Diagnostics { get; }
        internal CommandBatch Commands { get; }
    }
}
