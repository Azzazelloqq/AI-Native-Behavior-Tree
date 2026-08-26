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
        /// (agentCount, totalSteps) pairs actually measured on a real Player, does a
        /// freshly-seeded estimator's per-agent estimate land within the documented
        /// <see cref="NativeWorkEstimatorV1.CalibrationTolerance"/> of the actually-measured
        /// median cost for that same point? All 42 points (24 Windows + 18 Android) are the real
        /// Immediate-policy medians from
        /// <c>Benchmarks~/Phase4/Platform/Windows/Results/windows-player-scheduling-calibration-20260826.json</c>
        /// and
        /// <c>Benchmarks~/Phase4/Platform/Android/Results/android-player-scheduling-calibration-20260826.json</c>
        /// -- see <c>Planning~/Evidence/P4-004/README.md</c>'s 2026-08-26 addendum for the
        /// derivation. Supersedes the original 24-point fixture set drawn from Editor batchmode
        /// data (<c>cost-curves-windows-editor-20260820.json</c>), which no longer correlates now
        /// that <see cref="NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep"/> itself is
        /// Player-derived (Editor runs ~11x slower per step, so the old fixture's measured values
        /// would fail against the new coefficient by design, not by regression).
        /// </summary>
        [TestCase("Windows", "scheduling-baseline-empty-job", 16u, 64ul, 262.5)]
        [TestCase("Windows", "scheduling-baseline-empty-job", 64u, 256ul, 257.8125)]
        [TestCase("Windows", "scheduling-baseline-empty-job", 256u, 1024ul, 239.453125)]
        [TestCase("Windows", "scheduling-baseline-empty-job", 1024u, 4096ul, 254.8828125)]
        [TestCase("Windows", "shallow-tree-cheap-conditions", 16u, 368ul, 1418.75)]
        [TestCase("Windows", "shallow-tree-cheap-conditions", 64u, 1472ul, 1425.0)]
        [TestCase("Windows", "shallow-tree-cheap-conditions", 256u, 5888ul, 1385.15625)]
        [TestCase("Windows", "shallow-tree-cheap-conditions", 1024u, 23552ul, 1317.578125)]
        [TestCase("Windows", "deep-sequence-selector-traversal", 16u, 4528ul, 14100.0)]
        [TestCase("Windows", "deep-sequence-selector-traversal", 64u, 18112ul, 14862.5)]
        [TestCase("Windows", "deep-sequence-selector-traversal", 256u, 72448ul, 15224.21875)]
        [TestCase("Windows", "deep-sequence-selector-traversal", 1024u, 289792ul, 14613.96484375)]
        [TestCase("Windows", "wide-branching-frequent-failures", 16u, 128ul, 462.5)]
        [TestCase("Windows", "wide-branching-frequent-failures", 64u, 512ul, 462.5)]
        [TestCase("Windows", "wide-branching-frequent-failures", 256u, 2048ul, 473.828125)]
        [TestCase("Windows", "wide-branching-frequent-failures", 1024u, 8192ul, 488.671875)]
        [TestCase("Windows", "predominantly-running-actions", 16u, 80ul, 350.0)]
        [TestCase("Windows", "predominantly-running-actions", 64u, 320ul, 315.625)]
        [TestCase("Windows", "predominantly-running-actions", 256u, 1280ul, 323.828125)]
        [TestCase("Windows", "predominantly-running-actions", 1024u, 5120ul, 324.90234375)]
        [TestCase("Windows", "many-programs-small-populations", 16u, 64ul, 300.0)]
        [TestCase("Windows", "many-programs-small-populations", 64u, 256ul, 267.1875)]
        [TestCase("Windows", "many-programs-small-populations", 256u, 1024ul, 267.96875)]
        [TestCase("Windows", "many-programs-small-populations", 1024u, 4096ul, 266.2109375)]
        [TestCase("Android", "scheduling-baseline-empty-job", 16u, 64ul, 293.750)]
        [TestCase("Android", "scheduling-baseline-empty-job", 64u, 256ul, 287.500)]
        [TestCase("Android", "scheduling-baseline-empty-job", 256u, 1024ul, 285.547)]
        [TestCase("Android", "shallow-tree-cheap-conditions", 16u, 368ul, 1387.500)]
        [TestCase("Android", "shallow-tree-cheap-conditions", 64u, 1472ul, 1320.313)]
        [TestCase("Android", "shallow-tree-cheap-conditions", 256u, 5888ul, 1317.188)]
        [TestCase("Android", "deep-sequence-selector-traversal", 16u, 4528ul, 15137.500)]
        [TestCase("Android", "deep-sequence-selector-traversal", 64u, 18112ul, 15164.063)]
        [TestCase("Android", "deep-sequence-selector-traversal", 256u, 72448ul, 15206.641)]
        [TestCase("Android", "wide-branching-frequent-failures", 16u, 128ul, 450.000)]
        [TestCase("Android", "wide-branching-frequent-failures", 64u, 512ul, 450.000)]
        [TestCase("Android", "wide-branching-frequent-failures", 256u, 2048ul, 450.781)]
        [TestCase("Android", "predominantly-running-actions", 16u, 80ul, 293.750)]
        [TestCase("Android", "predominantly-running-actions", 64u, 320ul, 293.750)]
        [TestCase("Android", "predominantly-running-actions", 256u, 1280ul, 293.750)]
        [TestCase("Android", "many-programs-small-populations", 16u, 64ul, 262.500)]
        [TestCase("Android", "many-programs-small-populations", 64u, 256ul, 257.813)]
        [TestCase("Android", "many-programs-small-populations", 256u, 1024ul, 256.641)]
        public void EstimateCorrelatesWithRealPlayerMeasuredCostWithinTheDocumentedTolerance(
            string platform, string scenario, uint agentCount, ulong totalSteps, double measuredMedianNanosecondsPerAgent)
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(agentCount, totalSteps, out var failure), Is.True, failure.Code.ToString());
            Assert.That(estimator.TryEstimateWorkPerAgentNanoseconds(out var estimated, out failure), Is.True, failure.Code.ToString());

            var relativeError = System.Math.Abs(estimated - measuredMedianNanosecondsPerAgent) / measuredMedianNanosecondsPerAgent;
            Assert.That(relativeError, Is.LessThanOrEqualTo(NativeWorkEstimatorV1.CalibrationTolerance),
                platform + "/" + scenario + "@" + agentCount + ": estimated=" + estimated + " measured=" + measuredMedianNanosecondsPerAgent
                + " relativeError=" + relativeError);
        }
    }
}
