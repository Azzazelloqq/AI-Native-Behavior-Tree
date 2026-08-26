namespace AIBT
{
    /// <summary>
    /// Implements <c>Documentation~/execution-and-scheduling.md</c>'s work-estimation formula
    /// (<c>estimated work = runnable agents x expected node steps per agent x calibrated
    /// node-cost units</c>) as an explicit, inspectable model. One instance tracks the smoothed
    /// "expected node steps per agent" for one compiled-program identity/population -- the caller
    /// owns keying one estimator per distinct program, this type does not do that bookkeeping
    /// itself. Coefficients are fixed at calibration time and never adjusted online (see
    /// <see cref="CalibratedNanosecondsPerNodeStep"/>'s own comment); recalibrating means
    /// re-running <c>Benchmarks~/Phase4/CostCurves/</c> and updating the constants here, per this
    /// card's own forbidden-changes clause against runtime/online adaptation.
    /// </summary>
    internal struct NativeWorkEstimatorV1
    {
        /// <summary>
        /// Calibrated node-cost coefficient: nanoseconds of native lifecycle work per atomic
        /// semantic step (a `CompositeEntered`/`CompositeExited`/`DispatchRequired`/etc. step from
        /// <see cref="NativeLifecycleStepResultV1"/>). Originally derived from Editor batchmode
        /// data (678.75 ns/step) in <c>P4-004</c>; <c>P4-008</c>'s 2026-08-26 addendum found the
        /// real, non-development Player runs ~11-12x faster per step than Editor on this same
        /// workstation, and re-measured the figure directly on two real Players instead: a
        /// Windows x64 IL2CPP/Burst Standalone Player (median 61.82 ns/step, 24 Immediate-policy
        /// cases) and an Android ARM64 IL2CPP/Burst Player on a physical Google Pixel 10 Pro
        /// (median 58.75 ns/step, 18 Immediate-policy cases) -- within ~5% of each other despite
        /// being architecturally very different CPUs. This constant is the pooled median of all
        /// 42 of those real Player Immediate-policy samples combined. See
        /// <c>Benchmarks~/Phase4/CostCurves/README.md</c>'s "Addendum (2026-08-26)" section and
        /// <c>Planning~/Evidence/P4-004/README.md</c> for the original Editor-only derivation this
        /// supersedes. Immediate is used for calibration specifically because it has no batching
        /// overhead to contaminate the per-step figure (P4-002 already showed Immediate's
        /// per-agent cost is population-independent).
        /// </summary>
        internal const double CalibratedNanosecondsPerNodeStep = 60.275;

        /// <summary>
        /// Evidence-based tolerance from the original Editor-only calibration: re-deriving each of
        /// the 24 (scenario, agent count) points behind the prior 678.75 ns/step figure and
        /// comparing the model's estimate against the actually-measured median cost for that same
        /// point, the largest observed deviation was 8.7% (see
        /// <c>Planning~/Evidence/P4-004/README.md</c>). Not re-derived against the real-Player
        /// samples behind the current <see cref="CalibratedNanosecondsPerNodeStep"/> value --
        /// kept as the existing evidence-based figure pending a dedicated re-check.
        /// </summary>
        internal const double CalibrationTolerance = 0.10;

        private const double SmoothingAlpha = 0.25;

        /// <summary>
        /// A single observation can move the clamped input toward the prior estimate by at most
        /// this fraction before smoothing is applied, regardless of how large the raw spike is.
        /// Combined with <see cref="SmoothingAlpha"/>, one call can move the smoothed estimate by
        /// at most <c>SmoothingAlpha * MaxRelativeStepDeltaPerObservation</c> = 12.5%, however
        /// extreme the observed spike.
        /// </summary>
        private const double MaxRelativeStepDeltaPerObservation = 0.5;

        private double _smoothedStepsPerAgent;
        private bool _hasEstimate;

        internal bool HasEstimate => _hasEstimate;
        internal double SmoothedStepsPerAgent => _smoothedStepsPerAgent;

        /// <summary>
        /// Feeds one real observation (a completed round's <c>agentCount</c>/<c>totalSteps</c>,
        /// e.g. from a <see cref="NativeSameFramePhaseControllerV1"/> or
        /// <see cref="NativePipelinedPhaseControllerV1"/> publish) into the smoothed estimate. The
        /// first observation seeds the estimate directly; every later one is bounded per
        /// <see cref="MaxRelativeStepDeltaPerObservation"/> before an exponential-smoothing blend.
        /// </summary>
        internal bool TryObserve(uint agentCount, ulong totalSteps, out NativeRuntimeFailureV1 failure)
        {
            if (agentCount == 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }

            var observed = (double)totalSteps / agentCount;
            if (!_hasEstimate)
            {
                _smoothedStepsPerAgent = observed;
                _hasEstimate = true;
                failure = default;
                return true;
            }

            var lowerBound = _smoothedStepsPerAgent * (1.0 - MaxRelativeStepDeltaPerObservation);
            var upperBound = _smoothedStepsPerAgent * (1.0 + MaxRelativeStepDeltaPerObservation);
            var clamped = observed < lowerBound ? lowerBound : observed > upperBound ? upperBound : observed;
            _smoothedStepsPerAgent += SmoothingAlpha * (clamped - _smoothedStepsPerAgent);
            failure = default;
            return true;
        }

        /// <summary>The estimated nanoseconds of native lifecycle work for one agent, given every observation so far.</summary>
        internal bool TryEstimateWorkPerAgentNanoseconds(out double estimatedNanoseconds, out NativeRuntimeFailureV1 failure)
        {
            estimatedNanoseconds = 0;
            if (!_hasEstimate)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }

            estimatedNanoseconds = _smoothedStepsPerAgent * CalibratedNanosecondsPerNodeStep;
            failure = default;
            return true;
        }

        /// <summary><c>estimated work = runnable agents x expected node steps per agent x calibrated node-cost units</c>, in nanoseconds.</summary>
        internal bool TryEstimateWorkNanoseconds(uint runnableAgents, out double estimatedNanoseconds, out NativeRuntimeFailureV1 failure)
        {
            estimatedNanoseconds = 0;
            if (runnableAgents == 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }

            if (!TryEstimateWorkPerAgentNanoseconds(out var perAgent, out failure)) return false;
            estimatedNanoseconds = perAgent * runnableAgents;
            return true;
        }
    }
}
