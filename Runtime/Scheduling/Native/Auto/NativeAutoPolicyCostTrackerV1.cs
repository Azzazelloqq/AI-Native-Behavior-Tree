namespace AIBT
{
    /// <summary>
    /// Tracks one policy's smoothed, bounded recent nanoseconds-per-agent cost from real
    /// observations -- the same seed-then-clamp-then-blend mechanism as
    /// <see cref="NativeWorkEstimatorV1"/> (P4-004), applied to a directly measured cost figure
    /// instead of node steps. This is the lightweight adaptation mechanism
    /// <c>Documentation~/benchmarks.md</c>'s step 5 asks `P4-007` to test: a bounded, explainable,
    /// zero-managed-allocation feedback tracker, not an unbounded or opaque online-learning model.
    /// A single spike can move the smoothed estimate by at most 12.5% in one call, exactly as
    /// <see cref="NativeWorkEstimatorV1"/> already proves for its own smoothing.
    /// </summary>
    internal struct NativeAutoPolicyCostTrackerV1
    {
        private const double SmoothingAlpha = 0.25;
        private const double MaxRelativeCostDeltaPerObservation = 0.5;

        private double _smoothedNanosecondsPerAgent;
        private bool _hasEstimate;
        private uint _observationCount;

        internal bool HasEstimate => _hasEstimate;
        internal double SmoothedNanosecondsPerAgent => _smoothedNanosecondsPerAgent;
        internal uint ObservationCount => _observationCount;

        internal bool TryObserve(double measuredNanosecondsPerAgent, out NativeRuntimeFailureV1 failure)
        {
            if (measuredNanosecondsPerAgent <= 0)
            {
                failure = new NativeRuntimeFailureV1(
                    NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid,
                    NativeResourceKindV1.InstanceBudgetState);
                return false;
            }

            if (!_hasEstimate)
            {
                _smoothedNanosecondsPerAgent = measuredNanosecondsPerAgent;
                _hasEstimate = true;
            }
            else
            {
                var lowerBound = _smoothedNanosecondsPerAgent * (1.0 - MaxRelativeCostDeltaPerObservation);
                var upperBound = _smoothedNanosecondsPerAgent * (1.0 + MaxRelativeCostDeltaPerObservation);
                var clamped = measuredNanosecondsPerAgent < lowerBound ? lowerBound
                    : measuredNanosecondsPerAgent > upperBound ? upperBound : measuredNanosecondsPerAgent;
                _smoothedNanosecondsPerAgent += SmoothingAlpha * (clamped - _smoothedNanosecondsPerAgent);
            }

            if (_observationCount < uint.MaxValue) _observationCount++;
            failure = default;
            return true;
        }
    }
}
