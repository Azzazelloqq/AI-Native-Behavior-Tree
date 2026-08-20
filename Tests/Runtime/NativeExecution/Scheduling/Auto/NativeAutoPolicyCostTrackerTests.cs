using NUnit.Framework;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.Auto
{
    public sealed class NativeAutoPolicyCostTrackerTests
    {
        [Test]
        public void NonPositiveObservationIsRejected()
        {
            var tracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(tracker.TryObserve(0, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(tracker.TryObserve(-5, out failure), Is.False);
        }

        [Test]
        public void FirstObservationSeedsTheEstimateDirectly()
        {
            var tracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(tracker.TryObserve(1000, out var failure), Is.True, failure.Code.ToString());
            Assert.That(tracker.HasEstimate, Is.True);
            Assert.That(tracker.SmoothedNanosecondsPerAgent, Is.EqualTo(1000.0));
            Assert.That(tracker.ObservationCount, Is.EqualTo(1u));
        }

        [Test]
        public void SubsequentObservationsBlendViaExponentialSmoothing()
        {
            var tracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(tracker.TryObserve(1000, out var failure), Is.True, failure.Code.ToString());
            // Observed 1200 (within +-50% of 1000), alpha=0.25 -> 1000 + 0.25*(1200-1000) = 1050.
            Assert.That(tracker.TryObserve(1200, out failure), Is.True, failure.Code.ToString());
            Assert.That(tracker.SmoothedNanosecondsPerAgent, Is.EqualTo(1050.0).Within(1e-9));
            Assert.That(tracker.ObservationCount, Is.EqualTo(2u));
        }

        [Test]
        public void ASingleExtremeSpikeMovesTheSmoothedEstimateByAtMostTheDocumentedBound()
        {
            var tracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(tracker.TryObserve(1000, out var failure), Is.True, failure.Code.ToString());
            Assert.That(tracker.TryObserve(1_000_000, out failure), Is.True, failure.Code.ToString());
            var relativeSwing = (tracker.SmoothedNanosecondsPerAgent - 1000.0) / 1000.0;
            Assert.That(relativeSwing, Is.LessThanOrEqualTo(0.125 + 1e-9));
            Assert.That(tracker.SmoothedNanosecondsPerAgent, Is.EqualTo(1125.0).Within(1e-9));
        }
    }
}
