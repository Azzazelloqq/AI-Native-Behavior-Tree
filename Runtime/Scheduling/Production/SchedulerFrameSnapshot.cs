namespace AIBT
{
    public enum SchedulerFrameSelectionSource : byte
    {
        DirectHostPath = 0,
        NativeAutoSelector = 1,
    }

    public enum SchedulerFrameDisposition : byte
    {
        Executed = 0,
        PipelinedScheduled = 1,
        PipelinedAdvanced = 2,
        DeferredGlobalBudget = 3,
        DeferredProfileShare = 4,
        Failed = 5,
    }

    public enum SchedulerSelectionReason : byte
    {
        None = 0,
        ForcedByCaller = 1,
        BelowMinimumJobWorkload = 2,
        BudgetConfigured = 3,
        PipelinedPreferredForThroughput = 4,
        BatchedForSameFrameThroughput = 5,
        FallbackToOnlyAvailablePolicy = 6,
        PreferredOverBatchedByMeasuredCost = 7,
    }

    public enum SchedulerEstimateConfidence : byte
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }

    /// <summary>
    /// One immutable agent entry from the scheduler's most recently completed frame. Entries are
    /// held in a scheduler-owned reused buffer and copied out by value through TryGetFrameEntry.
    /// </summary>
    public readonly struct SchedulerFrameEntry
    {
        internal SchedulerFrameEntry(
            ulong hostInstanceId, SchedulingProfile profile, uint eligibleSinceFrame,
            bool hasDeadline, uint deadlineFrame, SchedulingPolicy selectedPolicy,
            SchedulerFrameSelectionSource selectionSource, SchedulerSelectionReason selectionReason,
            bool hasWorkEstimate, double expectedNodeStepsPerAgent,
            double estimatedWorkPerAgentNanoseconds, double estimatedTotalWorkNanoseconds,
            SchedulerEstimateConfidence confidence, uint batchSize, uint batchCount,
            double workerUtilizationProxy, bool hasConfiguredStepBudget,
            double configuredStepBudgetNanoseconds, bool exceedsConfiguredStepBudget,
            bool pipelinedLatencyAllowed, uint updateCadence,
            SchedulerFrameDisposition disposition, double consumedMicroseconds,
            ulong executedSteps, ulong observedLatencyFrames, NodeStatus? terminalOutcome,
            NativeRuntimeFailureV1 failure)
        {
            HostInstanceId = hostInstanceId;
            ProfileId = profile.Id;
            ProfileRevision = profile.Revision;
            EligibleSinceFrame = eligibleSinceFrame;
            HasDeadline = hasDeadline;
            DeadlineFrame = deadlineFrame;
            SelectedPolicy = selectedPolicy;
            SelectionSource = selectionSource;
            SelectionReason = selectionReason;
            HasWorkEstimate = hasWorkEstimate;
            ExpectedNodeStepsPerAgent = expectedNodeStepsPerAgent;
            EstimatedWorkPerAgentNanoseconds = estimatedWorkPerAgentNanoseconds;
            EstimatedTotalWorkNanoseconds = estimatedTotalWorkNanoseconds;
            Confidence = confidence;
            BatchSize = batchSize;
            BatchCount = batchCount;
            WorkerUtilizationProxy = workerUtilizationProxy;
            HasConfiguredStepBudget = hasConfiguredStepBudget;
            ConfiguredStepBudgetNanoseconds = configuredStepBudgetNanoseconds;
            ExceedsConfiguredStepBudget = exceedsConfiguredStepBudget;
            PipelinedLatencyAllowed = pipelinedLatencyAllowed;
            UpdateCadence = updateCadence;
            Disposition = disposition;
            ConsumedMicroseconds = consumedMicroseconds;
            ExecutedSteps = executedSteps;
            ObservedLatencyFrames = observedLatencyFrames;
            TerminalOutcome = terminalOutcome;
            Failure = failure;
        }

        public ulong HostInstanceId { get; }
        public string ProfileId { get; }
        public uint ProfileRevision { get; }
        public uint EligibleSinceFrame { get; }
        public bool HasDeadline { get; }
        public uint DeadlineFrame { get; }
        public SchedulingPolicy SelectedPolicy { get; }
        public SchedulerFrameSelectionSource SelectionSource { get; }
        public SchedulerSelectionReason SelectionReason { get; }
        public bool HasWorkEstimate { get; }
        public double ExpectedNodeStepsPerAgent { get; }
        public double EstimatedWorkPerAgentNanoseconds { get; }
        public double EstimatedTotalWorkNanoseconds { get; }
        public SchedulerEstimateConfidence Confidence { get; }
        public uint BatchSize { get; }
        public uint BatchCount { get; }
        public double WorkerUtilizationProxy { get; }
        public bool HasConfiguredStepBudget { get; }
        public double ConfiguredStepBudgetNanoseconds { get; }
        public bool ExceedsConfiguredStepBudget { get; }
        public bool PipelinedLatencyAllowed { get; }
        public uint UpdateCadence { get; }
        public SchedulerFrameDisposition Disposition { get; }
        public double ConsumedMicroseconds { get; }
        public ulong ExecutedSteps { get; }
        public ulong ObservedLatencyFrames { get; }
        public NodeStatus? TerminalOutcome { get; }
        public NativeRuntimeFailureV1 Failure { get; }
    }
}
