using NUnit.Framework;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.Estimation
{
    public sealed class NativeWorkEstimatorTests
    {
        [Test]
        public void EstimationFailsBeforeAnyObservation()
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.HasEstimate, Is.False);
            Assert.That(estimator.TryEstimateWorkPerAgentNanoseconds(out _, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(estimator.TryEstimateWorkNanoseconds(4, out _, out failure), Is.False);
        }

        [Test]
        public void ZeroAgentCountObservationIsRejected()
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(0, 100, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
        }

        [Test]
        public void FirstObservationSeedsTheEstimateDirectly()
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(16, 64, out var failure), Is.True, failure.Code.ToString());
            Assert.That(estimator.HasEstimate, Is.True);
            Assert.That(estimator.SmoothedStepsPerAgent, Is.EqualTo(4.0));
        }

        [Test]
        public void SubsequentObservationsBlendViaExponentialSmoothing()
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(10, 100, out var failure), Is.True, failure.Code.ToString());
            Assert.That(estimator.SmoothedStepsPerAgent, Is.EqualTo(10.0));

            // Observed 12 (within the +-50% clamp band of 10), alpha=0.25 -> 10 + 0.25*(12-10) = 10.5.
            Assert.That(estimator.TryObserve(10, 120, out failure), Is.True, failure.Code.ToString());
            Assert.That(estimator.SmoothedStepsPerAgent, Is.EqualTo(10.5).Within(1e-9));
        }

        [Test]
        public void ASingleExtremeSpikeMovesTheSmoothedEstimateByAtMostTheDocumentedBound()
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(10, 100, out var failure), Is.True, failure.Code.ToString());
            var before = estimator.SmoothedStepsPerAgent;

            // A 100x spike (observed steps/agent = 1000 against a smoothed estimate of 10) must
            // still only move the smoothed estimate by SmoothingAlpha * MaxRelativeStepDelta =
            // 0.25 * 0.5 = 12.5%, however extreme the raw observation is.
            Assert.That(estimator.TryObserve(1, 1000, out failure), Is.True, failure.Code.ToString());
            var after = estimator.SmoothedStepsPerAgent;
            var relativeSwing = (after - before) / before;
            Assert.That(relativeSwing, Is.LessThanOrEqualTo(0.125 + 1e-9),
                "A single spike must never swing the smoothed estimate more than 12.5% in one call.");
            Assert.That(after, Is.EqualTo(11.25).Within(1e-9));
        }

        [Test]
        public void EstimatedWorkScalesLinearlyWithRunnableAgents()
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(1, 4, out var failure), Is.True, failure.Code.ToString());
            Assert.That(estimator.TryEstimateWorkPerAgentNanoseconds(out var perAgent, out failure), Is.True, failure.Code.ToString());
            Assert.That(perAgent, Is.EqualTo(4.0 * NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep).Within(1e-9));
            Assert.That(estimator.TryEstimateWorkNanoseconds(8, out var total, out failure), Is.True, failure.Code.ToString());
            Assert.That(total, Is.EqualTo(perAgent * 8).Within(1e-9));
        }

        /// <summary>
        /// The correlation check this card's acceptance criteria requires: given the exact
        /// (agentCount, totalSteps) pairs P4-002 actually measured, does a freshly-seeded
        /// estimator's per-agent estimate land within the documented
        /// <see cref="NativeWorkEstimatorV1.CalibrationTolerance"/> of the actually-measured
        /// median cost for that same point? All 24 points are the real medians from
        /// <c>Benchmarks~/Phase4/CostCurves/Results/cost-curves-windows-editor-20260820.json</c>
        /// (Immediate policy) -- see <c>Planning~/Evidence/P4-004/README.md</c> for the derivation.
        /// </summary>
        [TestCase("scheduling-baseline-empty-job", 16u, 64ul, 2912.5)]
        [TestCase("scheduling-baseline-empty-job", 64u, 256ul, 2864.0625)]
        [TestCase("scheduling-baseline-empty-job", 256u, 1024ul, 2845.3125)]
        [TestCase("scheduling-baseline-empty-job", 1024u, 4096ul, 2909.1796875)]
        [TestCase("shallow-tree-cheap-conditions", 16u, 368ul, 15456.25)]
        [TestCase("shallow-tree-cheap-conditions", 64u, 1472ul, 15214.0625)]
        [TestCase("shallow-tree-cheap-conditions", 256u, 5888ul, 15375.78125)]
        [TestCase("shallow-tree-cheap-conditions", 1024u, 23552ul, 15485.25390625)]
        [TestCase("deep-sequence-selector-traversal", 16u, 4528ul, 177768.75)]
        [TestCase("deep-sequence-selector-traversal", 64u, 18112ul, 176895.3125)]
        [TestCase("deep-sequence-selector-traversal", 256u, 72448ul, 176699.21875)]
        [TestCase("deep-sequence-selector-traversal", 1024u, 289792ul, 178592.48046875)]
        [TestCase("wide-branching-frequent-failures", 16u, 128ul, 5112.5)]
        [TestCase("wide-branching-frequent-failures", 64u, 512ul, 5167.1875)]
        [TestCase("wide-branching-frequent-failures", 256u, 2048ul, 5104.296875)]
        [TestCase("wide-branching-frequent-failures", 1024u, 8192ul, 5157.421875)]
        [TestCase("predominantly-running-actions", 16u, 80ul, 3437.5)]
        [TestCase("predominantly-running-actions", 64u, 320ul, 3550.0)]
        [TestCase("predominantly-running-actions", 256u, 1280ul, 3346.484375)]
        [TestCase("predominantly-running-actions", 1024u, 5120ul, 3439.35546875)]
        [TestCase("many-programs-small-populations", 16u, 64ul, 2881.25)]
        [TestCase("many-programs-small-populations", 64u, 256ul, 2868.75)]
        [TestCase("many-programs-small-populations", 256u, 1024ul, 2922.65625)]
        [TestCase("many-programs-small-populations", 1024u, 4096ul, 2954.00390625)]
        public void EstimateCorrelatesWithP4002sMeasuredCostWithinTheDocumentedTolerance(
            string scenario, uint agentCount, ulong totalSteps, double measuredMedianNanosecondsPerAgent)
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(agentCount, totalSteps, out var failure), Is.True, failure.Code.ToString());
            Assert.That(estimator.TryEstimateWorkPerAgentNanoseconds(out var estimated, out failure), Is.True, failure.Code.ToString());

            var relativeError = System.Math.Abs(estimated - measuredMedianNanosecondsPerAgent) / measuredMedianNanosecondsPerAgent;
            Assert.That(relativeError, Is.LessThanOrEqualTo(NativeWorkEstimatorV1.CalibrationTolerance),
                scenario + "@" + agentCount + ": estimated=" + estimated + " measured=" + measuredMedianNanosecondsPerAgent
                + " relativeError=" + relativeError);
        }
    }
}
