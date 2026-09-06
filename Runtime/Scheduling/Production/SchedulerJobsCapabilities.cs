namespace AIBT
{
    /// <summary>Why <see cref="SchedulerJobsCapabilities.TryCreate"/> refused to build a capability set. Never silently clamped.</summary>
    public enum SchedulerJobsCapabilitiesValidationError : byte
    {
        None = 0,
        MinimumJobWorkloadNanosecondsMustBePositive,
        TargetBatchWorkNanosecondsMustBePositive,
        PolicyMinBatchSizeMustBeAtLeastOne,
        PolicyMaxBatchSizeBelowMinimum,
        MemoryLimitBatchSizeMustBeAtLeastOne,
    }

    /// <summary>
    /// Explicit, caller-authored tuning for <see cref="ProductionTreeScheduler"/>'s own Jobs-policy
    /// selection (<c>NativeAutoConfigurationV1</c>'s <c>MinimumJobWorkloadNanoseconds</c>/
    /// <c>TargetBatchWorkNanoseconds</c>/batch-size bounds) -- never a hidden default, per
    /// ADR-P7-033/P7-033's own "no hidden arbitrary budget, weight or latency default" clause.
    /// Until a caller sets this via <see cref="ProductionTreeScheduler.SetJobsCapabilities"/>,
    /// <c>BatchedJobsSameFrame</c>/<c>PipelinedJobs</c> are simply absent from the coordinator's
    /// own supported-policy set: an unmeasured batch-work target is never guessed, so a forced
    /// Jobs policy fails with a structured diagnostic rather than silently using an invented number.
    /// </summary>
    public readonly struct SchedulerJobsCapabilities
    {
        private SchedulerJobsCapabilities(
            double minimumJobWorkloadNanoseconds,
            double targetBatchWorkNanoseconds,
            uint policyMinBatchSize,
            uint policyMaxBatchSize,
            uint memoryLimitBatchSize)
        {
            MinimumJobWorkloadNanoseconds = minimumJobWorkloadNanoseconds;
            TargetBatchWorkNanoseconds = targetBatchWorkNanoseconds;
            PolicyMinBatchSize = policyMinBatchSize;
            PolicyMaxBatchSize = policyMaxBatchSize;
            MemoryLimitBatchSize = memoryLimitBatchSize;
        }

        internal double MinimumJobWorkloadNanoseconds { get; }
        internal double TargetBatchWorkNanoseconds { get; }
        internal uint PolicyMinBatchSize { get; }
        internal uint PolicyMaxBatchSize { get; }
        internal uint MemoryLimitBatchSize { get; }

        /// <summary>Validates and builds a capability set. Returns false with a structured reason on any invalid value; never clamps.</summary>
        public static bool TryCreate(
            double minimumJobWorkloadNanoseconds,
            double targetBatchWorkNanoseconds,
            uint policyMinBatchSize,
            uint policyMaxBatchSize,
            uint memoryLimitBatchSize,
            out SchedulerJobsCapabilities capabilities,
            out SchedulerJobsCapabilitiesValidationError error)
        {
            capabilities = default;
            if (!(minimumJobWorkloadNanoseconds > 0.0))
            {
                error = SchedulerJobsCapabilitiesValidationError.MinimumJobWorkloadNanosecondsMustBePositive;
                return false;
            }
            if (!(targetBatchWorkNanoseconds > 0.0))
            {
                error = SchedulerJobsCapabilitiesValidationError.TargetBatchWorkNanosecondsMustBePositive;
                return false;
            }
            if (policyMinBatchSize < 1u)
            {
                error = SchedulerJobsCapabilitiesValidationError.PolicyMinBatchSizeMustBeAtLeastOne;
                return false;
            }
            if (policyMaxBatchSize < policyMinBatchSize)
            {
                error = SchedulerJobsCapabilitiesValidationError.PolicyMaxBatchSizeBelowMinimum;
                return false;
            }
            if (memoryLimitBatchSize < 1u)
            {
                error = SchedulerJobsCapabilitiesValidationError.MemoryLimitBatchSizeMustBeAtLeastOne;
                return false;
            }
            capabilities = new SchedulerJobsCapabilities(
                minimumJobWorkloadNanoseconds, targetBatchWorkNanoseconds,
                policyMinBatchSize, policyMaxBatchSize, memoryLimitBatchSize);
            error = SchedulerJobsCapabilitiesValidationError.None;
            return true;
        }
    }
}
