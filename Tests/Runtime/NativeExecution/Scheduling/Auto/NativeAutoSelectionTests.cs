using NUnit.Framework;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.Auto
{
    public sealed class NativeAutoSelectionTests
    {
        private static NativeAutoConfigurationV1 Configuration(
            NativeAutoSupportedPoliciesV1 supportedPolicies = NativeAutoSupportedPoliciesV1.All,
            NativeAutoLatencyModeV1 latencyMode = NativeAutoLatencyModeV1.SameFrame,
            NativeAutoPolicyV1? forcedPolicy = null,
            double minimumJobWorkloadNanoseconds = 1000,
            double targetBatchWorkNanoseconds = 10000,
            uint policyMinBatchSize = 1,
            uint policyMaxBatchSize = 256,
            uint memoryLimitBatchSize = 256,
            uint workerCount = 8,
            ulong? updateBudgetSteps = null,
            uint updateCadence = 1)
            => new NativeAutoConfigurationV1(
                supportedPolicies, latencyMode, forcedPolicy, minimumJobWorkloadNanoseconds,
                targetBatchWorkNanoseconds, policyMinBatchSize, policyMaxBatchSize,
                memoryLimitBatchSize, workerCount, updateBudgetSteps, updateCadence);

        private static NativeAutoWorkloadV1 Workload(
            double expectedNodeStepsPerAgent = 10,
            double estimatedWorkPerAgentNanoseconds = 6787.5,
            uint observationCount = 5)
            => new NativeAutoWorkloadV1(expectedNodeStepsPerAgent, estimatedWorkPerAgentNanoseconds, observationCount);

        [Test]
        public void InvalidInputsAreRejected()
        {
            Assert.That(NativeAutoSelectionV1.TrySelect(
                Configuration(), Workload(), 0, out _, out var failure), Is.False,
                "Zero runnable agents is invalid.");
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(NativeAutoSelectionV1.TrySelect(
                Configuration(), Workload(estimatedWorkPerAgentNanoseconds: 0), 10, out _, out failure), Is.False,
                "A non-positive work estimate is invalid.");
            Assert.That(NativeAutoSelectionV1.TrySelect(
                Configuration(supportedPolicies: NativeAutoSupportedPoliciesV1.None), Workload(), 10, out _, out failure), Is.False,
                "No supported policies at all is invalid.");
        }

        [Test]
        public void ForcingAPolicyNotInTheSupportedSetReturnsAStructuredDiagnosticNotASilentSubstitution()
        {
            var configuration = Configuration(
                supportedPolicies: NativeAutoSupportedPoliciesV1.Immediate,
                forcedPolicy: NativeAutoPolicyV1.PipelinedJobs,
                latencyMode: NativeAutoLatencyModeV1.PipelinedAllowed);
            Assert.That(NativeAutoSelectionV1.TrySelect(configuration, Workload(), 10, out var explanation, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(explanation, Is.EqualTo(default(NativeAutoExplanationV1)),
                "No explanation is produced for a rejected force -- it must not silently fall back to a different policy.");
        }

        [Test]
        public void ForcingPipelinedJobsWithoutPermittingPipelinedLatencyIsRejected()
        {
            var configuration = Configuration(
                forcedPolicy: NativeAutoPolicyV1.PipelinedJobs,
                latencyMode: NativeAutoLatencyModeV1.SameFrame);
            Assert.That(NativeAutoSelectionV1.TrySelect(configuration, Workload(), 10, out _, out var failure), Is.False,
                "A forced PipelinedJobs selection must still respect the caller's own configured latency mode -- forcing is not implicit permission for a contradictory configuration.");
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
        }

        [Test]
        public void ForcingASupportedPolicyConsistentWithLatencyModeAlwaysWins()
        {
            var configuration = Configuration(forcedPolicy: NativeAutoPolicyV1.Budgeted);
            Assert.That(NativeAutoSelectionV1.TrySelect(configuration, Workload(), 10, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Budgeted));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.ForcedByCaller));
        }

        [Test]
        public void AutoNeverSelectsPipelinedJobsWhenLatencyModeIsSameFrame()
        {
            // Large enough estimated total work that, if pipelining were permitted, it would be
            // the preferred choice -- proving the latency gate, not just an unrelated tie-break.
            var configuration = Configuration(latencyMode: NativeAutoLatencyModeV1.SameFrame, minimumJobWorkloadNanoseconds: 1);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.Not.EqualTo(NativeAutoPolicyV1.PipelinedJobs));
        }

        [Test]
        public void BelowMinimumJobWorkloadSelectsImmediate()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1_000_000);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 10), 10, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Immediate));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.BelowMinimumJobWorkload));
            Assert.That(explanation.BatchSize, Is.EqualTo(0u));
            Assert.That(explanation.BatchCount, Is.EqualTo(0u));
            Assert.That(explanation.WorkerUtilizationProxy, Is.EqualTo(0.0));
        }

        [Test]
        public void ConfiguredUpdateBudgetSelectsBudgeted()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1, updateBudgetSteps: 500);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Budgeted));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.BudgetConfigured));
            Assert.That(explanation.HasConfiguredBudget, Is.True);
            Assert.That(explanation.ConfiguredUpdateBudgetNanoseconds,
                Is.EqualTo(500 * NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep).Within(1e-6));
        }

        [Test]
        public void ExceedsConfiguredBudgetIsReportedWhenEstimatedWorkIsHigher()
        {
            var configuration = Configuration(minimumJobWorkloadNanoseconds: 1, updateBudgetSteps: 1);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 1_000_000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ExceedsConfiguredBudget, Is.True);
        }

        [Test]
        public void LargeWorkloadWithPipelinedAllowedPrefersPipelinedJobs()
        {
            var configuration = Configuration(latencyMode: NativeAutoLatencyModeV1.PipelinedAllowed, minimumJobWorkloadNanoseconds: 1);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.PipelinedJobs));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.PipelinedPreferredForThroughput));
            Assert.That(explanation.BatchSize, Is.GreaterThan(0u));
            Assert.That(explanation.BatchCount, Is.GreaterThan(0u));
        }

        [Test]
        public void LargeWorkloadWithSameFrameRequiredPrefersImmediateOverBatchedByMeasuredCost()
        {
            // P6-019 recalibration: with Immediate supported (the default "All" set), a large
            // same-frame workload now prefers Immediate over BatchedJobsSameFrame -- the reverse of
            // the pre-recalibration rule this exact scenario used to exercise -- because P4-002's/
            // P4-006's own real cost curves showed Immediate cheaper in 24 of 24 measured points.
            var configuration = Configuration(latencyMode: NativeAutoLatencyModeV1.SameFrame, minimumJobWorkloadNanoseconds: 1);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Immediate));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.PreferredOverBatchedByMeasuredCost));
        }

        [Test]
        public void LargeWorkloadStillSelectsBatchedJobsSameFrameWhenItIsTheOnlySameFrameCapablePolicySupported()
        {
            // BatchedJobsSameFrame remains reachable -- P6-019 demotes it in priority, it does not
            // remove it -- when it is genuinely the only same-frame-capable policy the caller has
            // made available.
            var configuration = Configuration(
                supportedPolicies: NativeAutoSupportedPoliciesV1.BatchedJobsSameFrame,
                latencyMode: NativeAutoLatencyModeV1.SameFrame, minimumJobWorkloadNanoseconds: 1);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.BatchedJobsSameFrame));
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.BatchedForSameFrameThroughput));
            Assert.That(explanation.BatchSize, Is.GreaterThan(0u));
        }

        [Test]
        public void OnlyImmediateSupportedFallsBackToImmediate()
        {
            var configuration = Configuration(
                supportedPolicies: NativeAutoSupportedPoliciesV1.Immediate,
                latencyMode: NativeAutoLatencyModeV1.SameFrame, minimumJobWorkloadNanoseconds: 1);
            Assert.That(NativeAutoSelectionV1.TrySelect(
                configuration, Workload(estimatedWorkPerAgentNanoseconds: 100000), 1000, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.ChosenPolicy, Is.EqualTo(NativeAutoPolicyV1.Immediate));
            // P6-019 recalibration: Immediate is now reached via the evidence-based preference
            // branch (it would have won this comparison even if Batched were also supported), not
            // the old "only available policy" fallback -- FallbackToOnlyAvailablePolicy is no longer
            // reachable from TrySelect at all (it remains live only in TrySelectAdaptive's own
            // deliberately-unchanged fallback tail).
            Assert.That(explanation.Reason, Is.EqualTo(NativeAutoSelectionReasonV1.PreferredOverBatchedByMeasuredCost));
        }

        [TestCase(0u, "Low")]
        [TestCase(2u, "Low")]
        [TestCase(3u, "Medium")]
        [TestCase(9u, "Medium")]
        [TestCase(10u, "High")]
        [TestCase(1000u, "High")]
        public void ConfidenceBucketsFollowTheDocumentedObservationCountThresholds(uint observationCount, string expected)
        {
            Assert.That(NativeAutoSelectionV1.TrySelect(
                Configuration(), Workload(observationCount: observationCount), 10, out var explanation, out var failure), Is.True, failure.Code.ToString());
            Assert.That(explanation.Confidence.ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void RepeatedSelectionWithIdenticalInputsIsDeterministic()
        {
            var configuration = Configuration(latencyMode: NativeAutoLatencyModeV1.PipelinedAllowed);
            var workload = Workload(estimatedWorkPerAgentNanoseconds: 54321);
            Assert.That(NativeAutoSelectionV1.TrySelect(configuration, workload, 777, out var first, out var failure1), Is.True, failure1.Code.ToString());
            for (var iteration = 0; iteration < 20; iteration++)
            {
                Assert.That(NativeAutoSelectionV1.TrySelect(configuration, workload, 777, out var repeat, out var failure2), Is.True, failure2.Code.ToString());
                Assert.That(repeat.ChosenPolicy, Is.EqualTo(first.ChosenPolicy));
                Assert.That(repeat.Reason, Is.EqualTo(first.Reason));
                Assert.That(repeat.BatchSize, Is.EqualTo(first.BatchSize));
                Assert.That(repeat.BatchCount, Is.EqualTo(first.BatchCount));
                Assert.That(repeat.EstimatedTotalWorkNanoseconds, Is.EqualTo(first.EstimatedTotalWorkNanoseconds));
            }
        }

        /// <summary>
        /// "For at least the scenarios in P4-001's catalog, Auto's selection is deterministic and
        /// reproducible given identical inputs" -- real (agentCount, totalSteps) pairs from
        /// <c>Benchmarks~/Phase4/CostCurves/Results/cost-curves-windows-editor-20260820.json</c>,
        /// fed through a real <see cref="NativeWorkEstimatorV1"/> exactly as a caller would, then
        /// selected twice to prove reproducibility on the same real data P4-004 validated against.
        /// </summary>
        [TestCase("scheduling-baseline-empty-job", 1024u, 4096ul)]
        [TestCase("shallow-tree-cheap-conditions", 1024u, 23552ul)]
        [TestCase("deep-sequence-selector-traversal", 1024u, 289792ul)]
        [TestCase("wide-branching-frequent-failures", 1024u, 8192ul)]
        [TestCase("predominantly-running-actions", 1024u, 5120ul)]
        [TestCase("many-programs-small-populations", 1024u, 4096ul)]
        public void SelectionIsDeterministicAndReproducibleForEveryP4001Scenario(
            string scenario, uint agentCount, ulong totalSteps)
        {
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(agentCount, totalSteps, out var estimatorFailure), Is.True, estimatorFailure.Code.ToString());
            Assert.That(estimator.TryEstimateWorkPerAgentNanoseconds(out var estimatedNs, out estimatorFailure), Is.True, estimatorFailure.Code.ToString());
            var workload = new NativeAutoWorkloadV1(estimator.SmoothedStepsPerAgent, estimatedNs, 1);
            var configuration = Configuration(latencyMode: NativeAutoLatencyModeV1.PipelinedAllowed);

            Assert.That(NativeAutoSelectionV1.TrySelect(configuration, workload, agentCount, out var first, out var failure), Is.True,
                scenario + ": " + failure.Code);
            for (var iteration = 0; iteration < 5; iteration++)
            {
                Assert.That(NativeAutoSelectionV1.TrySelect(configuration, workload, agentCount, out var repeat, out failure), Is.True, failure.Code.ToString());
                Assert.That(repeat.ChosenPolicy, Is.EqualTo(first.ChosenPolicy), scenario);
                Assert.That(repeat.Reason, Is.EqualTo(first.Reason), scenario);
            }
        }
    }
}
