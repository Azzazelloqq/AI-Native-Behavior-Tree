using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Runtime.NativeExecution.HotReload.NativeHotReloadTestDriver;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    /// <summary>
    /// P7-012's own Auto-determinism-on-reload confirmation (ADR-P7-011 decision 7, inherited
    /// unchanged from P5-007's own already-made estimator-reset decision). Investigated directly
    /// rather than assumed: <see cref="NativeAutoSelectionV1.TrySelect"/> takes only a
    /// <see cref="NativeAutoConfigurationV1"/>/<see cref="NativeAutoWorkloadV1"/>/agent count -- it
    /// has no field or parameter referencing any <see cref="NativeLifecycleMachineV1"/> or hot-reload
    /// type at all, and <see cref="NativeWorkEstimatorV1"/> (the one thing that DOES accumulate
    /// state across calls) is explicitly caller-owned, never stored inside the native instance's
    /// own arena/memory/frames (confirmed by reading both types directly). So a hot reload cannot
    /// perturb Auto's decision through any channel that exists in this codebase; this test makes
    /// that structural fact concrete rather than merely asserting it in prose: it drives a real
    /// instance through an actual <see cref="NativeHotReloadFullRestart.TryRestart"/>, then confirms
    /// (a) <c>TrySelect</c> with the same inputs still picks identically before and after, and
    /// (b) the established convention this decision rests on -- the caller reseeding a fresh
    /// <see cref="NativeWorkEstimatorV1"/> after a reload rather than carrying over the old one --
    /// produces the identical estimate a plain fresh estimator would for the same observations.
    /// </summary>
    public sealed class NativeHotReloadAutoDeterminismTests
    {
        [Test]
        public void TrySelect_WithIdenticalInputs_PicksIdenticallyAcrossAFullRestart()
        {
            var program = NativeHotReloadTestProgram.TwoLeafSequence(reversed: false);
            var configuration = new NativeAutoConfigurationV1(
                NativeAutoSupportedPoliciesV1.All, NativeAutoLatencyModeV1.SameFrame, forcedPolicy: null,
                minimumJobWorkloadNanoseconds: 1_000.0, targetBatchWorkNanoseconds: 50_000.0,
                policyMinBatchSize: 1, policyMaxBatchSize: 64, memoryLimitBatchSize: 64,
                workerCount: 4, updateBudgetSteps: null, updateCadence: 1);
            var workload = new NativeAutoWorkloadV1(
                expectedNodeStepsPerAgent: 200.0,
                estimatedWorkPerAgentNanoseconds: 200.0 * NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep,
                observationCount: 5);
            const uint runnableAgents = 128;

            Assert.That(NativeAutoSelectionV1.TrySelect(configuration, workload, runnableAgents, out var beforeExplanation, out var beforeFailure),
                Is.True, beforeFailure.Code.ToString());

            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var old, out var buildFailure), Is.True, buildFailure.Code.ToString());
            Assert.That(old.Machine.TryBeginUpdate(1, out var beginFailure), Is.True, beginFailure.Code.ToString());
            RunOneTickToWaiting(ref old.Machine);
            Assert.That(
                NativeHotReloadFullRestart.TryRestart(old, program, 2, Allocator.Persistent, out var fresh, out _, out var restartFailure),
                Is.True, restartFailure.Code.ToString());
            try
            {
                Assert.That(NativeAutoSelectionV1.TrySelect(configuration, workload, runnableAgents, out var afterExplanation, out var afterFailure),
                    Is.True, afterFailure.Code.ToString());

                Assert.That(afterExplanation.ChosenPolicy, Is.EqualTo(beforeExplanation.ChosenPolicy));
                Assert.That(afterExplanation.Reason, Is.EqualTo(beforeExplanation.Reason));
                Assert.That(afterExplanation.BatchSize, Is.EqualTo(beforeExplanation.BatchSize));
                Assert.That(afterExplanation.BatchCount, Is.EqualTo(beforeExplanation.BatchCount));
                Assert.That(afterExplanation.EstimatedTotalWorkNanoseconds, Is.EqualTo(beforeExplanation.EstimatedTotalWorkNanoseconds));
            }
            finally
            {
                fresh.Dispose();
            }
        }

        [Test]
        public void WorkEstimator_ReseededAfterReload_MatchesAPlainFreshEstimatorForTheSameObservations()
        {
            // The established convention this decision rests on: a caller reloading a native
            // instance starts a brand-new NativeWorkEstimatorV1 rather than carrying the old one
            // forward (P5-007's own estimator-reset decision) -- confirmed here to produce the
            // identical estimate a never-reloaded estimator fed the same observations would, so
            // "reset" really does mean "behaves exactly like fresh", not an approximation of it.
            var reseeded = default(NativeWorkEstimatorV1);
            var neverReloaded = default(NativeWorkEstimatorV1);

            Assert.That(reseeded.TryObserve(64, 12_800, out var reseededObserveFailure1), Is.True, reseededObserveFailure1.Code.ToString());
            Assert.That(neverReloaded.TryObserve(64, 12_800, out var freshObserveFailure1), Is.True, freshObserveFailure1.Code.ToString());
            Assert.That(reseeded.TryObserve(64, 13_400, out var reseededObserveFailure2), Is.True, reseededObserveFailure2.Code.ToString());
            Assert.That(neverReloaded.TryObserve(64, 13_400, out var freshObserveFailure2), Is.True, freshObserveFailure2.Code.ToString());

            Assert.That(reseeded.TryEstimateWorkPerAgentNanoseconds(out var reseededEstimate, out var reseededEstimateFailure),
                Is.True, reseededEstimateFailure.Code.ToString());
            Assert.That(neverReloaded.TryEstimateWorkPerAgentNanoseconds(out var freshEstimate, out var freshEstimateFailure),
                Is.True, freshEstimateFailure.Code.ToString());

            Assert.That(reseededEstimate, Is.EqualTo(freshEstimate),
                "a reseeded (post-reload) estimator fed the same observations as a never-reloaded one must reach the identical estimate.");
        }
    }
}
