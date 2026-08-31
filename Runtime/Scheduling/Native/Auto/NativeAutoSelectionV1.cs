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
                // P6-019 recalibration: Immediate/Budgeted are tried BEFORE BatchedJobsSameFrame --
                // reversed from the original rule, which unconditionally preferred batching here
                // with no real cost comparison. P4-002's/P4-006's own measured cost curves showed
                // Immediate/Budgeted cheaper than BatchedJobsSameFrame in 24 of 24 measured points
                // (16-1024 agents); this reorder is fully grounded in that evidence, with no new
                // numeric threshold introduced, per the owner's own approved scope for this card.
                else if (IsSupported(candidates, NativeAutoPolicyV1.Immediate))
                {
                    chosen = NativeAutoPolicyV1.Immediate;
                    reason = NativeAutoSelectionReasonV1.PreferredOverBatchedByMeasuredCost;
                }
                else if (IsSupported(candidates, NativeAutoPolicyV1.Budgeted))
                {
                    chosen = NativeAutoPolicyV1.Budgeted;
                    reason = NativeAutoSelectionReasonV1.PreferredOverBatchedByMeasuredCost;
                }
                else if (IsSupported(candidates, NativeAutoPolicyV1.BatchedJobsSameFrame))
                {
                    chosen = NativeAutoPolicyV1.BatchedJobsSameFrame;
                    reason = NativeAutoSelectionReasonV1.BatchedForSameFrameThroughput;
                }
                else
                {
                    failure = new NativeRuntimeFailureV1(
                        NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                        NativeResourceKindV1.InstanceBudgetState);
                    return false;
                }
            }

            return BuildExplanation(configuration, workload, runnableAgents, chosen, reason, out explanation, out failure);
        }

        /// <summary>
        /// `P4-007`'s lightweight-adaptation experiment (`OQ-006`, rejected -- kept only for its own
        /// experimental test coverage, never called by any shipped production caller). Deliberately
        /// NOT recalibrated by `P6-019`: that card's own scope is <see cref="TrySelect"/>'s
        /// deterministic rule specifically, and this method's own fallback tail (unchanged, still
        /// preferring `BatchedJobsSameFrame` before Immediate/Budgeted below) is exactly what its own
        /// existing tests exercise on purpose to demonstrate the deterministic-rule mistake the
        /// adaptive mechanism was built to route around. Identical to
        /// <see cref="TrySelect"/> for a forced policy, and identical for the
        /// below-minimum-workload and explicit-budget priors (a caller's explicit signals are not
        /// second-guessed by tracked cost data). The one difference: at the exact decision point
        /// that <c>Planning~/Evidence/P4-006/</c> found costly (choosing among
        /// `BatchedJobsSameFrame`/`PipelinedJobs`/`Immediate`/`Budgeted` for a same-frame- or
        /// pipeline-eligible large workload), this method compares each viable candidate's own
        /// <see cref="NativeAutoPolicyCostTrackerV1"/> -- its smoothed, bounded recent real cost --
        /// and picks whichever is currently cheapest, but only once at least two candidates have
        /// an established estimate; with fewer than two, it falls back to this method's own
        /// unchanged copy of the pre-`P6-019` deterministic rule (a cold start has no real data to
        /// compare) -- no longer literally identical to <see cref="TrySelect"/> after `P6-019`'s own
        /// recalibration of that method specifically; see this method's own updated remarks above.
        /// </summary>
        internal static bool TrySelectAdaptive(
            in NativeAutoConfigurationV1 configuration,
            in NativeAutoWorkloadV1 workload,
            uint runnableAgents,
            in NativeAutoPolicyCostTrackerV1 immediateTracker,
            in NativeAutoPolicyCostTrackerV1 budgetedTracker,
            in NativeAutoPolicyCostTrackerV1 batchedTracker,
            in NativeAutoPolicyCostTrackerV1 pipelinedTracker,
            out NativeAutoExplanationV1 explanation,
            out NativeRuntimeFailureV1 failure)
        {
            explanation = default;
            if (configuration.ForcedPolicy.HasValue)
            {
                return TrySelect(configuration, workload, runnableAgents, out explanation, out failure);
            }

            if (runnableAgents == 0 || workload.EstimatedWorkPerAgentNanoseconds <= 0
                || configuration.SupportedPolicies == NativeAutoSupportedPoliciesV1.None)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }

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

            NativeAutoPolicyV1 chosen;
            NativeAutoSelectionReasonV1 reason;
            var estimatedTotal = workload.EstimatedWorkPerAgentNanoseconds * runnableAgents;

            if (estimatedTotal < configuration.MinimumJobWorkloadNanoseconds
                && IsSupported(candidates, NativeAutoPolicyV1.Immediate))
            {
                chosen = NativeAutoPolicyV1.Immediate;
                reason = NativeAutoSelectionReasonV1.BelowMinimumJobWorkload;
            }
            else if (configuration.UpdateBudgetSteps.HasValue && IsSupported(candidates, NativeAutoPolicyV1.Budgeted))
            {
                chosen = NativeAutoPolicyV1.Budgeted;
                reason = NativeAutoSelectionReasonV1.BudgetConfigured;
            }
            else if (TryPickLowestTrackedCost(candidates, immediateTracker, budgetedTracker, batchedTracker, pipelinedTracker, out var tracked))
            {
                chosen = tracked;
                reason = NativeAutoSelectionReasonV1.AdaptiveLowestTrackedCost;
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

            return BuildExplanation(configuration, workload, runnableAgents, chosen, reason, out explanation, out failure);
        }

        private static bool TryPickLowestTrackedCost(
            NativeAutoSupportedPoliciesV1 candidates,
            in NativeAutoPolicyCostTrackerV1 immediateTracker,
            in NativeAutoPolicyCostTrackerV1 budgetedTracker,
            in NativeAutoPolicyCostTrackerV1 batchedTracker,
            in NativeAutoPolicyCostTrackerV1 pipelinedTracker,
            out NativeAutoPolicyV1 chosen)
        {
            chosen = default;
            var trackedCount = 0;
            var bestCost = double.PositiveInfinity;

            if (IsSupported(candidates, NativeAutoPolicyV1.Immediate) && immediateTracker.HasEstimate)
            {
                trackedCount++;
                if (immediateTracker.SmoothedNanosecondsPerAgent < bestCost)
                {
                    bestCost = immediateTracker.SmoothedNanosecondsPerAgent;
                    chosen = NativeAutoPolicyV1.Immediate;
                }
            }
            if (IsSupported(candidates, NativeAutoPolicyV1.Budgeted) && budgetedTracker.HasEstimate)
            {
                trackedCount++;
                if (budgetedTracker.SmoothedNanosecondsPerAgent < bestCost)
                {
                    bestCost = budgetedTracker.SmoothedNanosecondsPerAgent;
                    chosen = NativeAutoPolicyV1.Budgeted;
                }
            }
            if (IsSupported(candidates, NativeAutoPolicyV1.BatchedJobsSameFrame) && batchedTracker.HasEstimate)
            {
                trackedCount++;
                if (batchedTracker.SmoothedNanosecondsPerAgent < bestCost)
                {
                    bestCost = batchedTracker.SmoothedNanosecondsPerAgent;
                    chosen = NativeAutoPolicyV1.BatchedJobsSameFrame;
                }
            }
            if (IsSupported(candidates, NativeAutoPolicyV1.PipelinedJobs) && pipelinedTracker.HasEstimate)
            {
                trackedCount++;
                if (pipelinedTracker.SmoothedNanosecondsPerAgent < bestCost)
                {
                    bestCost = pipelinedTracker.SmoothedNanosecondsPerAgent;
                    chosen = NativeAutoPolicyV1.PipelinedJobs;
                }
            }

            return trackedCount >= 2;
        }

        private static bool BuildExplanation(
            in NativeAutoConfigurationV1 configuration,
            in NativeAutoWorkloadV1 workload,
            uint runnableAgents,
            NativeAutoPolicyV1 chosen,
            NativeAutoSelectionReasonV1 reason,
            out NativeAutoExplanationV1 explanation,
            out NativeRuntimeFailureV1 failure)
        {
            explanation = default;
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
