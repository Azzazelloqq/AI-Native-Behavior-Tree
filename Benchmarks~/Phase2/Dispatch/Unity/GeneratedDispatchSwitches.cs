using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AOT;
using Unity.Burst;

namespace AIBT.Benchmarks.Phase2.Dispatch
{
    internal static partial class DispatchBenchmarkRunner
    {
        // Exact switch widths are intentionally source-visible benchmark inputs.

        [BurstCompile(CompileSynchronously = true)]
        [MonoPInvokeCallback(typeof(DispatchFunction))]
        private static long ExecuteGeneratedDispatchSwitch1(
            ref BurstExecutionBatch batch)
        {
            if (IsManagedFallback())
            {
                return FailureCode(13, BurstContextResult.InvalidStatus);
            }

            long processed = 0;
            while (true)
            {
                var gate = TryAcquireBenchmarkFrame(
                    ref batch,
                    out var catalogCaseIndex,
                    out var frame,
                    out var hasWork,
                    out var failureStage);
                if (gate != BurstContextResult.Success)
                {
                    return FailureCode(failureStage, gate);
                }

                if (!hasWork) break;

                gate = DispatchGeneratedSwitch1(
                    catalogCaseIndex, ref batch, in frame);
                if (gate != BurstContextResult.Success)
                {
                    return FailureCode(3, gate);
                }

                processed++;
            }

            return ValidateCompletedExecution(in batch, processed);
        }

        private static BurstContextResult DispatchGeneratedSwitch1(
            uint catalogCaseIndex,
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame)
        {
            switch (catalogCaseIndex)
            {
                case 0u:
                    return ExecuteMicroCallback(ref batch, in frame);
                default:
                    return BurstContextResult.TypeMismatch;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        [MonoPInvokeCallback(typeof(DispatchFunction))]
        private static long ExecuteGeneratedDispatchSwitch16(
            ref BurstExecutionBatch batch)
        {
            if (IsManagedFallback())
            {
                return FailureCode(13, BurstContextResult.InvalidStatus);
            }

            long processed = 0;
            while (true)
            {
                var gate = TryAcquireBenchmarkFrame(
                    ref batch,
                    out var catalogCaseIndex,
                    out var frame,
                    out var hasWork,
                    out var failureStage);
                if (gate != BurstContextResult.Success)
                {
                    return FailureCode(failureStage, gate);
                }

                if (!hasWork) break;

                gate = DispatchGeneratedSwitch16(
                    catalogCaseIndex, ref batch, in frame);
                if (gate != BurstContextResult.Success)
                {
                    return FailureCode(3, gate);
                }

                processed++;
            }

            return ValidateCompletedExecution(in batch, processed);
        }

        private static BurstContextResult DispatchGeneratedSwitch16(
            uint catalogCaseIndex,
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame)
        {
            switch (catalogCaseIndex)
            {
                case 0u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 1u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 2u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 3u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 4u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 5u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 6u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 7u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 8u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 9u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 10u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 11u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 12u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 13u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 14u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 15u:
                    return ExecuteMicroCallback(ref batch, in frame);
                default:
                    return BurstContextResult.TypeMismatch;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        [MonoPInvokeCallback(typeof(DispatchFunction))]
        private static long ExecuteGeneratedDispatchSwitch128(
            ref BurstExecutionBatch batch)
        {
            if (IsManagedFallback())
            {
                return FailureCode(13, BurstContextResult.InvalidStatus);
            }

            long processed = 0;
            while (true)
            {
                var gate = TryAcquireBenchmarkFrame(
                    ref batch,
                    out var catalogCaseIndex,
                    out var frame,
                    out var hasWork,
                    out var failureStage);
                if (gate != BurstContextResult.Success)
                {
                    return FailureCode(failureStage, gate);
                }

                if (!hasWork) break;

                gate = DispatchGeneratedSwitch128(
                    catalogCaseIndex, ref batch, in frame);
                if (gate != BurstContextResult.Success)
                {
                    return FailureCode(3, gate);
                }

                processed++;
            }

            return ValidateCompletedExecution(in batch, processed);
        }

        private static BurstContextResult DispatchGeneratedSwitch128(
            uint catalogCaseIndex,
            ref BurstExecutionBatch batch,
            in BurstDispatchFrame frame)
        {
            switch (catalogCaseIndex)
            {
                case 0u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 1u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 2u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 3u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 4u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 5u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 6u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 7u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 8u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 9u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 10u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 11u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 12u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 13u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 14u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 15u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 16u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 17u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 18u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 19u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 20u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 21u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 22u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 23u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 24u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 25u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 26u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 27u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 28u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 29u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 30u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 31u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 32u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 33u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 34u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 35u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 36u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 37u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 38u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 39u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 40u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 41u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 42u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 43u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 44u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 45u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 46u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 47u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 48u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 49u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 50u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 51u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 52u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 53u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 54u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 55u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 56u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 57u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 58u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 59u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 60u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 61u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 62u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 63u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 64u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 65u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 66u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 67u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 68u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 69u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 70u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 71u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 72u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 73u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 74u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 75u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 76u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 77u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 78u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 79u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 80u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 81u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 82u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 83u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 84u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 85u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 86u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 87u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 88u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 89u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 90u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 91u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 92u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 93u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 94u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 95u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 96u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 97u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 98u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 99u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 100u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 101u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 102u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 103u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 104u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 105u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 106u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 107u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 108u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 109u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 110u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 111u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 112u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 113u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 114u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 115u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 116u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 117u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 118u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 119u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 120u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 121u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 122u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 123u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 124u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 125u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 126u:
                    return ExecuteMicroCallback(ref batch, in frame);
                case 127u:
                    return ExecuteMicroCallback(ref batch, in frame);
                default:
                    return BurstContextResult.TypeMismatch;
            }
        }
    }
}
