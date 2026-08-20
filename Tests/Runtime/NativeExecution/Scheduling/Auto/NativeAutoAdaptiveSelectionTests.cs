using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.Auto
{
    public sealed class NativeAutoAdaptiveSelectionTests
    {
        private static NativeAutoConfigurationV1 Configuration(
            NativeAutoSupportedPoliciesV1 supportedPolicies = NativeAutoSupportedPoliciesV1.All,
            NativeAutoLatencyModeV1 latencyMode = NativeAutoLatencyModeV1.SameFrame,
            NativeAutoPolicyV1? forcedPolicy = null,
            double minimumJobWorkloadNanoseconds = 1000,
            ulong? updateBudgetSteps = null)
            => new NativeAutoConfigurationV1(
                supportedPolicies, latencyMode, forcedPolicy, minimumJobWorkloadNanoseconds,
                10000, 1, 256, 256, 8, updateBudgetSteps, 1);

        private static NativeAutoWorkloadV1 Workload(double estimatedWorkPerAgentNanoseconds = 6787.5)
            => new NativeAutoWorkloadV1(10, estimatedWorkPerAgentNanoseconds, 5);

        [Test]
        public void ForcedPolicyDelegatesToTrySelectUnchanged()
        {
            var configuration = Configuration(forcedPolicy: NativeAutoPolicyV1.Budgeted);
            var noTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, Workload(), 100, noTracker, noTracker, noTracker, noTracker,
                out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Budgeted));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.ForcedByCaller));
        }

        [Test]
        public void ColdStartWithFewerThanTwoTrackedCandidatesFallsBackToTheDeterministicRule()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1);
            var noTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000,
                noTracker, noTracker, noTracker, noTracker,
                out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.BatchedJobsSameFrame));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.BatchedForSameFrameThroughput),
                "With no real tracked data (cold start), the adaptive path must fall back to the exact same deterministic rule as TrySelect.");
        }

        [Test]
        public void WithTwoOrMoreTrackedCandidatesThePolicyWithTheLowestSmoothedCostWins()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1);
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(immediateTracker.TryObserve(3000, out var trackerFailure), Is.True, trackerFailure.Code.ToString());
            Assert.That(batchedTracker.TryObserve(50000, out trackerFailure), Is.True, trackerFailure.Code.ToString());

            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000,
                immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Immediate),
                "Immediate's tracked cost (3000) is lower than BatchedJobsSameFrame's (50000), so it must win even though the deterministic rule alone would have preferred batching.");
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.AdaptiveLowestTrackedCost));
        }

        [Test]
        public void BelowMinimumWorkloadStillWinsRegardlessOfTrackedData()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1_000_000);
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(batchedTracker.TryObserve(10, out var trackerFailure), Is.True, trackerFailure.Code.ToString());
            Assert.That(immediateTracker.TryObserve(20, out trackerFailure), Is.True, trackerFailure.Code.ToString());
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();

            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 10), 10,
                immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Immediate));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.BelowMinimumJobWorkload));
        }

        [Test]
        public void ConfiguredBudgetStillWinsRegardlessOfTrackedData()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1, updateBudgetSteps: 500);
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(batchedTracker.TryObserve(10, out var trackerFailure), Is.True, trackerFailure.Code.ToString());
            Assert.That(immediateTracker.TryObserve(20, out trackerFailure), Is.True, trackerFailure.Code.ToString());
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();

            Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000,
                immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Budgeted));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.BudgetConfigured));
        }

        [Test]
        public void SteadyStateAdaptiveSelectionIntroducesNoManagedAllocation()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1);
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(immediateTracker.TryObserve(3000, out var trackerFailure), Is.True, trackerFailure.Code.ToString());
            Assert.That(batchedTracker.TryObserve(50000, out trackerFailure), Is.True, trackerFailure.Code.ToString());
            var workload = Workload(estimatedWorkPerAgentNanoseconds: 100000);

            var success = false;
            Assert.That(() =>
            {
                success = NativeAutoSelectionV1.TrySelectAdaptive(
                    configuration, workload, 1000, immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                    out _, out _);
            }, GcAllocIs.Not.AllocatingGCMemory());
            Assert.That(success, Is.True);
        }

        [Test]
        public void RepeatedAdaptiveSelectionWithIdenticalInputsIsDeterministic()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1);
            var immediateTracker = new NativeAutoPolicyCostTrackerV1();
            var batchedTracker = new NativeAutoPolicyCostTrackerV1();
            var emptyTracker = new NativeAutoPolicyCostTrackerV1();
            Assert.That(immediateTracker.TryObserve(3000, out var trackerFailure), Is.True, trackerFailure.Code.ToString());
            Assert.That(batchedTracker.TryObserve(50000, out trackerFailure), Is.True, trackerFailure.Code.ToString());
            var workload = Workload(estimatedWorkPerAgentNanoseconds: 100000);

            NativeAutoExplanationV1 first = default;
            for (var iteration = 0; iteration < 10; iteration++)
            {
                Assert.That(NativeAutoSelectionV1.TrySelectAdaptive(
                    configuration, workload, 1000, immediateTracker, emptyTracker, batchedTracker, emptyTracker,
                    out var explanation, out var failure), Is.True, failure.Code.ToString());
                if (iteration == 0) first = explanation;
                else
                {
                    Assert.That(explanation.ChosenPolicy, Is.EqualTo(first.ChosenPolicy));
                    Assert.That(explanation.Reason, Is.EqualTo(first.Reason));
                }
            }
        }
    }
}
