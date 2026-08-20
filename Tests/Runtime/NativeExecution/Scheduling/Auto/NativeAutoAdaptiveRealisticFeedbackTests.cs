using NUnit.Framework;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.Auto
{
    /// <summary>
    /// P4-007's decisive experiment for `OQ-006`. `NativeAutoAdaptiveSelectionTests` already proves
    /// the adaptive comparison is mechanically correct once both trackers have real data -- but a
    /// real caller only observes the cost of the policy it actually ran each round, never an
    /// untried alternative's cost. These tests simulate that realistic, single-observer feedback
    /// loop with real <c>Benchmarks~/Phase4/CostCurves/Results/cost-curves-windows-editor-20260820.json</c>
    /// numbers (Immediate vs. BatchedJobsSameFrame, `wide-branching-frequent-failures`/1024 agents:
    /// 5,157.42 vs. 74,158.79 ns/agent -- one of P4-006's worst-gap cases) and show the reactive
    /// design this card built (no exploration mechanism) can never discover a cheaper policy it has
    /// not already tried, so it cannot close a cold-start deterministic mistake on its own.
    /// </summary>
    public sealed class NativeAutoAdaptiveRealisticFeedbackTests
    {
        private const double RealImmediateNanosecondsPerAgent = 5157.421875;
        private const double RealBatchedNanosecondsPerAgent = 74158.7890625;

        [Test]
        public void AReactiveTrackerFedOnlyItsOwnChoicesNeverEscapesAColdStartMistake()
        {
            var configuration = new NativeAutoConfigurationV1(
                NativeAutoSupportedPoliciesV1.All, NativeAutoLatencyModeV1.SameFrame, null,
                1, 10000, 1, 256, 256, 8, null, 1);
            var workload = new NativeAutoWorkloadV1(8, 5157.421875, 5);
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();

            for (var round = 0; round < 50; round++)
            {
                Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                    configuration, workload, 1024, immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                    out var explanation, out var failure), Is.True, failure.Code.ToString());

                // Realistic single-observer feedback: only the policy Auto actually chose this
                // round gets a real cost observation -- an untried alternative's cost is unknown,
                // exactly like a real running system.
                if (explanation.ChosenPolicy == NativeAutoPolicyV1.Immediate)
                    Assert.That(immediateTracker.TryObserve(RealImmediateNanosecondsPerAgent, out var observeFailure), Is.True, observeFailure.Code.ToString());
                else if (explanation.ChosenPolicy == NativeAutoPolicyV1.BatchedJobsSameFrame)
                    Assert.That(batchedTracker.TryObserve(RealBatchedNanosecondsPerAgent, out var observeFailure), Is.True, observeFailure.Code.ToString());

                if (round == 0)
                {
                    Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.BatchedJobsSameFrame),
                        "Cold start reproduces P4-006's actual mistake: the deterministic fallback picks BatchedJobsSameFrame first.");
                }
            }

            Assert.That(batchedTracker.HasEstimate, Is.True);
            Assert.That(immediateTracker.HasEstimate, Is.False,
                "With no exploration mechanism, Immediate is never chosen even once across 50 rounds, so its tracker never receives a single real observation -- the adaptive comparison (which needs at least two tracked candidates) can never activate.");

            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, workload, 1024, immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                out var finalExplanation, out var finalFailure), Is.True, finalFailure.Code.ToString());
            Assert.That(finalExplanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.BatchedJobsSameFrame),
                "After 50 rounds of realistic single-observer feedback, the reactive adaptive design is still stuck on its original cold-start mistake -- it never discovers Immediate is roughly 14x cheaper here, because it never tries Immediate to find out.");
        }

        [Test]
        public void GivenExternallySuppliedObservationsOfBothPoliciesTheAdaptiveComparisonDoesCorrectItself()
        {
            // Contrast case: if BOTH policies' real costs happen to become known (e.g. an external
            // exploration mechanism, or P4-002-style periodic re-benchmarking, neither of which
            // this card builds), the comparison itself is correct -- confirming the flaw identified
            // above is specifically the missing exploration mechanism, not a bug in the comparison.
            var configuration = new NativeAutoConfigurationV1(
                NativeAutoSupportedPoliciesV1.All, NativeAutoLatencyModeV1.SameFrame, null,
                1, 10000, 1, 256, 256, 8, null, 1);
            var workload = new NativeAutoWorkloadV1(8, RealImmediateNanosecondsPerAgent, 5);
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(immediateTracker.TryObserve(RealImmediateNanosecondsPerAgent, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchedTracker.TryObserve(RealBatchedNanosecondsPerAgent, out failure), Is.True, failure.Code.ToString());

            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, workload, 1024, immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                out var explanation, out failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Immediate));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.AdaptiveLowestTrackedCost));
        }
    }
}
