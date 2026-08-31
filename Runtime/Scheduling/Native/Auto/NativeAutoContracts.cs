namespace AIBT
{
    /// <summary>The four accepted native execution policies `Auto` selects among, per `Documentation~/execution-and-scheduling.md`'s policy table.</summary>
    internal enum NativeAutoPolicyV1 : byte
    {
        Immediate = 0,
        Budgeted = 1,
        BatchedJobsSameFrame = 2,
        PipelinedJobs = 3,
    }

    /// <summary>
    /// Which policies are available to select among. This is a caller-supplied capability
    /// declaration, not a live platform/backend detection -- `Documentation~/specifications/platform-backends-v1.md`'s
    /// Web backend does not exist as code anywhere in this package yet, so "the active backend's
    /// capability set" can only be represented as an input a caller (whichever integration layer
    /// eventually exists per backend) provides, never something this selector probes for itself.
    /// </summary>
    [System.Flags]
    internal enum NativeAutoSupportedPoliciesV1 : byte
    {
        None = 0,
        Immediate = 1 << 0,
        Budgeted = 1 << 1,
        BatchedJobsSameFrame = 1 << 2,
        PipelinedJobs = 1 << 3,
        All = Immediate | Budgeted | BatchedJobsSameFrame | PipelinedJobs,
    }

    /// <summary>
    /// Whether the caller has explicitly permitted cross-frame latency. `PipelinedJobs` is never
    /// selected automatically unless this is <see cref="PipelinedAllowed"/> --
    /// <c>Documentation~/execution-and-scheduling.md</c>: "Automatic policy never opts into extra
    /// semantic latency unless the user explicitly permits it." A caller-forced `PipelinedJobs`
    /// selection is exempt from this gate: forcing a specific policy is itself the caller's
    /// explicit permission for its latency.
    /// </summary>
    internal enum NativeAutoLatencyModeV1 : byte
    {
        SameFrame = 0,
        PipelinedAllowed = 1,
    }

    /// <summary>
    /// A coarse, documented confidence bucket derived from how many real observations have fed
    /// the work estimate this selection is based on -- not a statistical claim, an inspectable
    /// rule: fewer than 3 observations is <see cref="Low"/>, fewer than 10 is
    /// <see cref="Medium"/>, otherwise <see cref="High"/>.
    /// </summary>
    internal enum NativeAutoConfidenceV1 : byte
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>The explainable reason `Auto` (or a caller's force) chose a policy -- never a black-box score.</summary>
    internal enum NativeAutoSelectionReasonV1 : byte
    {
        ForcedByCaller = 0,
        BelowMinimumJobWorkload = 1,
        BudgetConfigured = 2,
        PipelinedPreferredForThroughput = 3,
        BatchedForSameFrameThroughput = 4,
        FallbackToOnlyAvailablePolicy = 5,

        /// <summary>
        /// `P4-007`'s lightweight-adaptation experiment: chosen because its
        /// <see cref="NativeAutoPolicyCostTrackerV1"/> reported the lowest smoothed recent cost
        /// among candidates with an established estimate -- never used until at least two
        /// candidates have real observations (see <see cref="NativeAutoSelectionV1.TrySelectAdaptive"/>).
        /// </summary>
        AdaptiveLowestTrackedCost = 6,

        /// <summary>
        /// `P6-019`'s deterministic recalibration: `Immediate`/`Budgeted` are preferred over
        /// `BatchedJobsSameFrame` for same-frame-required throughput, because `P4-002`'s and
        /// `P4-006`'s own real measured cost curves showed `Immediate`/`Budgeted` cheaper in 24 of
        /// 24 measured points (16-1024 agents) -- `BatchedJobsSameFrame`'s per-chunk overhead never
        /// amortized at any tested scale on that workstation. This is a static, evidence-grounded
        /// preference order, never runtime-tracked cost (that is
        /// <see cref="AdaptiveLowestTrackedCost"/>'s own, separate, rejected experiment).
        /// </summary>
        PreferredOverBatchedByMeasuredCost = 7,
    }

    /// <summary>
    /// The full override surface `Documentation~/execution-and-scheduling.md`'s "Explainability
    /// and overrides" section specifies: force a specific policy, minimum job workload, target
    /// batch work, batch bounds, update budget, latency mode, and tree-specific update cadence.
    /// <see cref="UpdateCadence"/> is recorded and echoed back in
    /// <see cref="NativeAutoExplanationV1"/> for inspectability, but nothing in this card acts on
    /// it -- driving an actual per-tree update cadence is an integration-layer concern outside
    /// this selector's scope, and this type does not claim otherwise.
    /// </summary>
    internal readonly struct NativeAutoConfigurationV1
    {
        internal NativeAutoConfigurationV1(
            NativeAutoSupportedPoliciesV1 supportedPolicies,
            NativeAutoLatencyModeV1 latencyMode,
            NativeAutoPolicyV1? forcedPolicy,
            double minimumJobWorkloadNanoseconds,
            double targetBatchWorkNanoseconds,
            uint policyMinBatchSize,
            uint policyMaxBatchSize,
            uint memoryLimitBatchSize,
            uint workerCount,
            ulong? updateBudgetSteps,
            uint updateCadence)
        {
            SupportedPolicies = supportedPolicies;
            LatencyMode = latencyMode;
            ForcedPolicy = forcedPolicy;
            MinimumJobWorkloadNanoseconds = minimumJobWorkloadNanoseconds;
            TargetBatchWorkNanoseconds = targetBatchWorkNanoseconds;
            PolicyMinBatchSize = policyMinBatchSize;
            PolicyMaxBatchSize = policyMaxBatchSize;
            MemoryLimitBatchSize = memoryLimitBatchSize;
            WorkerCount = workerCount;
            UpdateBudgetSteps = updateBudgetSteps;
            UpdateCadence = updateCadence;
        }

        internal NativeAutoSupportedPoliciesV1 SupportedPolicies { get; }
        internal NativeAutoLatencyModeV1 LatencyMode { get; }
        internal NativeAutoPolicyV1? ForcedPolicy { get; }
        internal double MinimumJobWorkloadNanoseconds { get; }
        internal double TargetBatchWorkNanoseconds { get; }
        internal uint PolicyMinBatchSize { get; }
        internal uint PolicyMaxBatchSize { get; }
        internal uint MemoryLimitBatchSize { get; }
        internal uint WorkerCount { get; }
        internal ulong? UpdateBudgetSteps { get; }
        internal uint UpdateCadence { get; }
    }

    /// <summary>
    /// The real work estimate this selection is based on -- `expectedNodeStepsPerAgent` and
    /// `estimatedWorkPerAgentNanoseconds` come from a caller-owned <c>NativeWorkEstimatorV1</c>
    /// (P4-004), and <paramref name="observationCount"/>Count from the caller's own count of how
    /// many times it has called that estimator's <c>TryObserve</c>. This selector consumes an
    /// already-computed estimate; it does not own or wrap the estimator itself.
    /// </summary>
    internal readonly struct NativeAutoWorkloadV1
    {
        internal NativeAutoWorkloadV1(
            double expectedNodeStepsPerAgent,
            double estimatedWorkPerAgentNanoseconds,
            uint observationCount)
        {
            ExpectedNodeStepsPerAgent = expectedNodeStepsPerAgent;
            EstimatedWorkPerAgentNanoseconds = estimatedWorkPerAgentNanoseconds;
            ObservationCount = observationCount;
        }

        internal double ExpectedNodeStepsPerAgent { get; }
        internal double EstimatedWorkPerAgentNanoseconds { get; }
        internal uint ObservationCount { get; }
    }

    /// <summary>
    /// The explainability surface this card narrows to fields with a real, verifiable data source
    /// today: chosen policy and reason, workload estimate and confidence, batch size/count, a
    /// worker-utilization proxy, expected node steps, and budget comparison. `commands`, `wakeups`,
    /// `deferred agents`, and a real per-batch scheduling-cost figure are not included -- no
    /// counting infrastructure for the first three exists anywhere in
    /// <c>Runtime/Scheduling/Native/</c> (command/wakeup counting is a leaf/Commands-subsystem
    /// concern, not a scheduler one), and `P4-004`'s estimator models only per-step cost, never
    /// per-batch Job-scheduling overhead -- adding either is a documented gap for a later card, not
    /// a silently faked field here (the same discipline `P4-001`'s unimplemented scenario
    /// placeholders already established).
    /// </summary>
    internal readonly struct NativeAutoExplanationV1
    {
        internal NativeAutoExplanationV1(
            NativeAutoPolicyV1 chosenPolicy,
            NativeAutoSelectionReasonV1 reason,
            double expectedNodeStepsPerAgent,
            double estimatedWorkPerAgentNanoseconds,
            double estimatedTotalWorkNanoseconds,
            NativeAutoConfidenceV1 confidence,
            uint batchSize,
            uint batchCount,
            double workerUtilizationProxy,
            bool hasConfiguredBudget,
            double configuredUpdateBudgetNanoseconds,
            bool exceedsConfiguredBudget,
            NativeAutoLatencyModeV1 latencyMode,
            uint updateCadence)
        {
            ChosenPolicy = chosenPolicy;
            Reason = reason;
            ExpectedNodeStepsPerAgent = expectedNodeStepsPerAgent;
            EstimatedWorkPerAgentNanoseconds = estimatedWorkPerAgentNanoseconds;
            EstimatedTotalWorkNanoseconds = estimatedTotalWorkNanoseconds;
            Confidence = confidence;
            BatchSize = batchSize;
            BatchCount = batchCount;
            WorkerUtilizationProxy = workerUtilizationProxy;
            HasConfiguredBudget = hasConfiguredBudget;
            ConfiguredUpdateBudgetNanoseconds = configuredUpdateBudgetNanoseconds;
            ExceedsConfiguredBudget = exceedsConfiguredBudget;
            LatencyMode = latencyMode;
            UpdateCadence = updateCadence;
        }

        internal NativeAutoPolicyV1 ChosenPolicy { get; }
        internal NativeAutoSelectionReasonV1 Reason { get; }
        internal double ExpectedNodeStepsPerAgent { get; }
        internal double EstimatedWorkPerAgentNanoseconds { get; }
        internal double EstimatedTotalWorkNanoseconds { get; }
        internal NativeAutoConfidenceV1 Confidence { get; }

        /// <summary>0 when the chosen policy does not batch (Immediate/Budgeted) -- not applicable, not a real batch of size zero.</summary>
        internal uint BatchSize { get; }

        /// <summary>0 when the chosen policy does not batch.</summary>
        internal uint BatchCount { get; }

        /// <summary><c>min(BatchCount, workerCount) / workerCount</c> when batched, 0 otherwise -- a proxy, not measured worker occupancy (Unity exposes no such telemetry).</summary>
        internal double WorkerUtilizationProxy { get; }

        internal bool HasConfiguredBudget { get; }
        internal double ConfiguredUpdateBudgetNanoseconds { get; }
        internal bool ExceedsConfiguredBudget { get; }
        internal NativeAutoLatencyModeV1 LatencyMode { get; }
        internal uint UpdateCadence { get; }
    }
}
