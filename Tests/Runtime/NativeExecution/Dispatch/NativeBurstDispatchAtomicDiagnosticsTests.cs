using System.Threading;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    public sealed class NativeBurstDispatchAtomicDiagnosticsTests
    {
        [Test]
        public void DirectAndNestedNativeListFieldsShareOneAtomicClaimAcrossParallelJobCopies()
        {
            using (var claim = new NativeList<int>(1, Allocator.TempJob))
            using (var directResults = new NativeArray<int>(
                2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            using (var nestedResults = new NativeArray<int>(
                2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            {
                claim.Add(0);

                new DirectNativeListCasJob
                {
                    Claim = claim,
                    Results = directResults,
                }.Schedule(directResults.Length, 1).Complete();
                AssertSingleWinner(directResults, claim[0], "direct NativeList field");

                var resetClaim = claim;
                resetClaim[0] = 0;
                new NestedNativeListCasJob
                {
                    Carrier = new NativeListClaimCarrier { Claim = claim },
                    Results = nestedResults,
                }.Schedule(nestedResults.Length, 1).Complete();
                AssertSingleWinner(nestedResults, claim[0], "NativeList field in nested struct");
            }
        }

        [Test]
        public void BatchBackingRawCasWinsOnceBeforeBridgeAcquisitionUsesTheSameClaim()
        {
            using (var scenario = NativeBurstDispatchBridgeTests.Scenario.Create())
            using (var rawCasResults = new NativeArray<long>(
                2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            using (var bridgeResults = new NativeArray<int>(
                2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            {
                var runtime = scenario.Batch.Runtime;
                var executionClaim = runtime.ExecutionClaim;
                var frameClaim = runtime.FrameCompletionClaim;
                Assert.That(executionClaim.Equals(frameClaim), Is.False);
                Assert.That(executionClaim.Length, Is.EqualTo(1));
                Assert.That(frameClaim.Length, Is.EqualTo(1));
                Assert.That(executionClaim[0], Is.Zero);
                Assert.That(frameClaim[0], Is.Zero);

                frameClaim[0] = 2;
                Assert.That(frameClaim[0], Is.EqualTo(2));
                Assert.That(executionClaim[0], Is.Zero,
                    "mutating the frame claim must not mutate the execution claim");
                frameClaim[0] = 0;

                Assert.That(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(
                    in scenario.Batch,
                    out var instanceOrdinal,
                    out var runtimeNodeIndex,
                    out var catalogCaseIndex,
                    out var phase,
                    out var hasWork), Is.EqualTo(BurstContextResult.Success));
                Assert.That(hasWork, Is.True);
                Assert.That(executionClaim[0], Is.EqualTo(1));

                new BatchBackingRawCasJob
                {
                    Batch = scenario.Batch,
                    Results = rawCasResults,
                }.Schedule(rawCasResults.Length, 1).Complete();
                AssertSingleLongWinner(rawCasResults, frameClaim[0], "Batch.Runtime raw frame claim");
                Assert.That(executionClaim[0], Is.EqualTo(1),
                    "raw frame CAS must not mutate the execution claim");

                frameClaim[0] = 0;
                new BatchBridgeAcquireJob
                {
                    Batch = scenario.Batch,
                    InstanceOrdinal = instanceOrdinal,
                    RuntimeNodeIndex = runtimeNodeIndex,
                    CatalogCaseIndex = catalogCaseIndex,
                    Phase = phase,
                    Results = bridgeResults,
                }.Schedule(bridgeResults.Length, 1).Complete();

                var control = runtime.Control[0];
                var observation =
                    $"bridge results [{(BurstContextResult)bridgeResults[0]}, {(BurstContextResult)bridgeResults[1]}], "
                    + $"execution claim {executionClaim[0]}, frame claim {frameClaim[0]}, "
                    + $"state {control.State}, next frame {control.NextFrameId}, active frame {control.ActiveFrameId}";
                var successes = 0;
                var phaseViolations = 0;
                for (var index = 0; index < bridgeResults.Length; index++)
                {
                    var result = (BurstContextResult)bridgeResults[index];
                    if (result == BurstContextResult.Success) successes++;
                    if (result == BurstContextResult.PhaseViolation) phaseViolations++;
                }

                var cleanupBatch = scenario.Batch;
                var cleanupResult = BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref cleanupBatch,
                    instanceOrdinal,
                    runtimeNodeIndex,
                    catalogCaseIndex,
                    phase,
                    out var cleanupFrame);
                if (cleanupResult == BurstContextResult.Success)
                {
                    BurstGeneratedRuntimeBridge.TryFailDispatch(
                        ref cleanupBatch,
                        in cleanupFrame,
                        BurstContextResult.PhaseViolation);
                }

                Assert.That(successes, Is.EqualTo(1), observation);
                Assert.That(phaseViolations, Is.EqualTo(1), observation);
            }
        }

        private static void AssertSingleWinner(
            NativeArray<int> results,
            int finalClaim,
            string shape)
        {
            var winners = 0;
            var losers = 0;
            for (var index = 0; index < results.Length; index++)
            {
                if (results[index] == 0) winners++;
                if (results[index] == 2) losers++;
            }

            var observation = $"{shape}: prior values [{results[0]}, {results[1]}], final {finalClaim}";
            Assert.That(winners, Is.EqualTo(1), observation);
            Assert.That(losers, Is.EqualTo(1), observation);
            Assert.That(finalClaim, Is.EqualTo(2), observation);
        }

        private static void AssertSingleLongWinner(
            NativeArray<long> results,
            long finalClaim,
            string shape)
        {
            var winners = 0;
            var losers = 0;
            for (var index = 0; index < results.Length; index++)
            {
                if (results[index] == 0L) winners++;
                if (results[index] == -1L) losers++;
            }

            var observation = $"{shape}: prior values [{results[0]}, {results[1]}], final {finalClaim}";
            Assert.That(winners, Is.EqualTo(1), observation);
            Assert.That(losers, Is.EqualTo(1), observation);
            Assert.That(finalClaim, Is.EqualTo(-1L), observation);
        }

        private struct NativeListClaimCarrier
        {
            [NativeDisableContainerSafetyRestriction]
            internal NativeList<int> Claim;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct DirectNativeListCasJob : IJobParallelFor
        {
            [NativeDisableContainerSafetyRestriction]
            internal NativeList<int> Claim;

            internal NativeArray<int> Results;

            public void Execute(int index)
            {
                Results[index] = Interlocked.CompareExchange(
                    ref Claim.ElementAt(0),
                    2,
                    0);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct NestedNativeListCasJob : IJobParallelFor
        {
            internal NativeListClaimCarrier Carrier;
            internal NativeArray<int> Results;

            public void Execute(int index)
            {
                Results[index] = Interlocked.CompareExchange(
                    ref Carrier.Claim.ElementAt(0),
                    2,
                    0);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BatchBackingRawCasJob : IJobParallelFor
        {
            internal BurstExecutionBatch Batch;
            internal NativeArray<long> Results;

            public void Execute(int index)
            {
                var claim = Batch.Runtime.FrameCompletionClaim;
                Results[index] = Interlocked.CompareExchange(
                    ref claim.ElementAt(0),
                    -1L,
                    0L);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BatchBridgeAcquireJob : IJobParallelFor
        {
            internal BurstExecutionBatch Batch;
            internal uint InstanceOrdinal;
            internal uint RuntimeNodeIndex;
            internal uint CatalogCaseIndex;
            internal BurstCallbackPhase Phase;
            internal NativeArray<int> Results;

            public void Execute(int index)
            {
                var batch = Batch;
                Results[index] = (int)BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                    ref batch,
                    InstanceOrdinal,
                    RuntimeNodeIndex,
                    CatalogCaseIndex,
                    Phase,
                    out _);
            }
        }
    }
}
