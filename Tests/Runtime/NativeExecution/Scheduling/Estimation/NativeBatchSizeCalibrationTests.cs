using NUnit.Framework;

namespace AIBT.Tests.Runtime.NativeExecution.Scheduling.Estimation
{
    public sealed class NativeBatchSizeCalibrationTests
    {
        [Test]
        public void InvalidInputsAreRejected()
        {
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                0, 10, 1, 100, 100, 50, 1, out _, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 0, 1, 100, 100, 50, 1, out _, out failure), Is.False);
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 10, 10, 5, 100, 50, 1, out _, out failure), Is.False,
                "policyMaxBatchSize below policyMinBatchSize is invalid.");
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 10, 1, 100, 100, 0, 1, out _, out failure), Is.False,
                "Zero runnable agents is invalid.");
        }

        [Test]
        public void RawFormulaAppliesWhenWithinAllLimits()
        {
            // target/estimate = 1000/10 = 100, inside [1,200] policy and 200 memory limit, and
            // with 1 worker there is no load-balancing floor to apply.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 10, 1, 200, 200, 50, 1, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(100u));
        }

        [Test]
        public void ClampsToThePolicyMinimumWhenTheRawFormulaIsSmaller()
        {
            // target/estimate = 1000/100 = 10, below the policy minimum of 20.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 100, 20, 200, 200, 50, 1, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(20u));
        }

        [Test]
        public void ClampsToThePolicyMaximumWhenTheRawFormulaIsLarger()
        {
            // target/estimate = 10000/10 = 1000, above the policy maximum of 200.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                10000, 10, 1, 200, 500, 50, 1, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(200u));
        }

        [Test]
        public void MemoryLimitWinsWhenItIsStricterThanThePolicyMaximum()
        {
            // Policy would allow up to 200, but memory can only hold 40 -- memory wins.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                10000, 10, 1, 200, 40, 50, 1, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(40u));
        }

        [Test]
        public void MemoryLimitWinsEvenWhenItIsBelowThePolicyMinimum()
        {
            // Policy demands at least 20, but memory can only hold 5 -- a batch the memory budget
            // cannot hold can never be scheduled regardless of policy preference, so memory wins.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 10, 20, 200, 5, 50, 1, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(5u));
        }

        [Test]
        public void BatchSizeIsShrunkToUseEveryAvailableWorkerWhenThePopulationSupportsIt()
        {
            // Raw formula gives batchSize=100 for 800 agents -> only 8 batches, but 16 workers are
            // available and the population (800) comfortably supports 16 batches -- shrink toward
            // ceil(800/16)=50 so every worker gets used, never below the policy/memory floor.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 10, 1, 500, 500, 800, 16, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(50u));
        }

        [Test]
        public void LoadBalancingNeverShrinksBatchSizeBelowThePolicyOrMemoryFloor()
        {
            // Same shape as above, but the policy minimum (60) is above the load-balancing target
            // (50) -- the floor wins, even though that means fewer than 16 batches.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                1000, 10, 60, 500, 500, 800, 16, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(60u));
        }

        [Test]
        public void LoadBalancingDoesNothingWhenTheRawResultAlreadyUsesEnoughWorkers()
        {
            // 800 agents / batchSize 40 = 20 batches, already >= the 16 available workers.
            Assert.That(NativeBatchSizeCalibrationV1.TryComputeBatchSize(
                400, 10, 1, 500, 500, 800, 16, out var batchSize, out var failure), Is.True, failure.Code.ToString());
            Assert.That(batchSize, Is.EqualTo(40u));
        }
    }
}
