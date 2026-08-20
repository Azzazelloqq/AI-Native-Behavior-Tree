namespace AIBT
{
    /// <summary>
    /// Deterministic, explainable selection among the four accepted native execution policies.
    /// Every branch of <see cref="TrySelect"/> is a plain, inspectable comparison -- never a
    /// black-box score -- and every selection carries a <see cref="NativeAutoSelectionReasonV1"/>
    /// a caller or test can check directly. This type owns selection only: it does not own the
    /// work estimator (`P4-004`), does not construct or drive any policy's own controller
    /// (`P2-019`/`P4-003`), and performs no runtime/online adaptation of the rule itself -- the
    /// rule below is the fixed baseline `P4-007`'s conditional autotuning card (`OQ-006`) compares
    /// against, never adjusted live by this type.
    /// </summary>
    internal static class NativeAutoSelectionV1
    {
        internal static bool TrySelect(
            in NativeAutoConfigurationV1 configuration,
            in NativeAutoWorkloadV1 workload,
            uint runnableAgents,
            out NativeAutoExplanationV1 explanation,
            out NativeRuntimeFailureV1 failure)
        {
            explanation = default;
            if (runnableAgents == 0 || workload.EstimatedWorkPerAgentNanoseconds <= 0
                || configuration.SupportedPolicies == NativeAutoSupportedPoliciesV1.None)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }

            NativeAutoPolicyV1 chosen;
            NativeAutoSelectionReasonV1 reason;

            if (configuration.ForcedPolicy.HasValue)
            {
                chosen = configuration.ForcedPolicy.Value;
                if (!IsSupported(configuration.SupportedPolicies, chosen))
                {
                    // Structured diagnostic, never a silent substitution with a different policy.
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.InstanceBudgetState);
                    return false;
                }

                if (chosen == NativeAutoPolicyV1.PipelinedJobs
                    && configuration.LatencyMode != NativeAutoLatencyModeV1.PipelinedAllowed)
                {
                    // Forcing PipelinedJobs while LatencyMode says SameFrame is an internal
                    // configuration contradiction, not implicit permission -- rejected with a
                    // structured diagnostic rather than silently honoring the force over the
                    // caller's own stated latency requirement (Documentation~/execution-and-scheduling.md's
                    // "never does so silently" guarantee applies regardless of how a policy was chosen).
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.InstanceBudgetState);
                    return false;
                }

                reason = NativeAutoSelectionReasonV1.ForcedByCaller;
            }
            else
            {
                var candidates = configuration.SupportedPolicies
                    & (configuration.LatencyMode == NativeAutoLatencyModeV1.PipelinedAllowed
                        ? NativeAutoSupportedPoliciesV1.All
                        : NativeAutoSupportedPoliciesV1.All & ~NativeAutoSupportedPoliciesV1.PipelinedJobs);
                if (candidates == NativeAutoSupportedPoliciesV1.None)
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.InstanceBudgetState);
                    return false;
                }

                var estimatedTotal = workload.EstimatedWorkPerAgentNanoseconds * runnableAgents;
                if (estimatedTotal < configuration.MinimumJobWorkloadNanoseconds
                    && IsSupported(candidates, NativeAutoPolicyV1.Immediate))
                {
                    chosen = NativeAutoPolicyV1.Immediate;
                    reason = NativeAutoSelectionReasonV1.BelowMinimumJobWorkload;
                }
                else if (configuration.UpdateBudgetSteps.HasValue
                    && IsSupported(candidates, NativeAutoPolicyV1.Budgeted))
                {
                    chosen = NativeAutoPolicyV1.Budgeted;
                    reason = NativeAutoSelectionReasonV1.BudgetConfigured;
                }
                else if (configuration.LatencyMode == NativeAutoLatencyModeV1.PipelinedAllowed
                    && IsSupported(candidates, NativeAutoPolicyV1.PipelinedJobs))
                {
                    chosen = NativeAutoPolicyV1.PipelinedJobs;
                    reason = NativeAutoSelectionReasonV1.PipelinedPreferredForThroughput;
                }
                else if (IsSupported(candidates, NativeAutoPolicyV1.BatchedJobsSameFrame))
                {
                    chosen = NativeAutoPolicyV1.BatchedJobsSameFrame;
                    reason = NativeAutoSelectionReasonV1.BatchedForSameFrameThroughput;
                }
                else if (IsSupported(candidates, NativeAutoPolicyV1.Immediate))
                {
                    chosen = NativeAutoPolicyV1.Immediate;
                    reason = NativeAutoSelectionReasonV1.FallbackToOnlyAvailablePolicy;
                }
                else if (IsSupported(candidates, NativeAutoPolicyV1.Budgeted))
                {
                    chosen = NativeAutoPolicyV1.Budgeted;
                    reason = NativeAutoSelectionReasonV1.FallbackToOnlyAvailablePolicy;
                }
                else
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.InstanceBudgetState);
                    return false;
                }
            }

            var estimatedTotalWork = workload.EstimatedWorkPerAgentNanoseconds * runnableAgents;
            var batches = chosen == NativeAutoPolicyV1.BatchedJobsSameFrame || chosen == NativeAutoPolicyV1.PipelinedJobs;
            uint batchSize = 0;
            uint batchCount = 0;
            var workerUtilizationProxy = 0.0;
            if (batches)
            {
                if (!NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                        configuration.TargetBatchWorkNanoseconds,
                        workload.EstimatedWorkPerAgentNanoseconds,
                        configuration.PolicyMinBatchSize,
                        configuration.PolicyMaxBatchSize,
                        configuration.MemoryLimitBatchSize,
                        runnableAgents,
                        configuration.WorkerCount,
                        out batchSize,
                        out failure))
                {
                    return false;
                }

                batchCount = (runnableAgents + batchSize - 1) / batchSize;
                if (configuration.WorkerCount > 0)
                {
                    var utilized = batchCount < configuration.WorkerCount ? batchCount : configuration.WorkerCount;
                    workerUtilizationProxy = (double)utilized / configuration.WorkerCount;
                }
            }

            var confidence = workload.ObservationCount < 3 ? NativeAutoConfidenceV1.Low
                : workload.ObservationCount < 10 ? NativeAutoConfidenceV1.Medium
                : NativeAutoConfidenceV1.High;

            var hasBudget = configuration.UpdateBudgetSteps.HasValue;
            var configuredBudgetNanoseconds = hasBudget
                ? configuration.UpdateBudgetSteps.Value * NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep
                : 0.0;
            var exceedsBudget = hasBudget && estimatedTotalWork > configuredBudgetNanoseconds;

            explanation = new NativeAutoExplanationV1(
                chosen, reason, workload.ExpectedNodeStepsPerAgent, workload.EstimatedWorkPerAgentNanoseconds,
                estimatedTotalWork, confidence, batchSize, batchCount, workerUtilizationProxy,
                hasBudget, configuredBudgetNanoseconds, exceedsBudget, configuration.LatencyMode,
                configuration.UpdateCadence);
            failure = default;
            return true;
        }

        private static bool IsSupported(NativeAutoSupportedPoliciesV1 supported, NativeAutoPolicyV1 policy)
        {
            var flag = policy switch
            {
                NativeAutoPolicyV1.Immediate => NativeAutoSupportedPoliciesV1.Immediate,
                NativeAutoPolicyV1.Budgeted => NativeAutoSupportedPoliciesV1.Budgeted,
                NativeAutoPolicyV1.BatchedJobsSameFrame => NativeAutoSupportedPoliciesV1.BatchedJobsSameFrame,
                NativeAutoPolicyV1.PipelinedJobs => NativeAutoSupportedPoliciesV1.PipelinedJobs,
                _ => NativeAutoSupportedPoliciesV1.None,
            };
            return (supported & flag) != 0;
        }
    }
}
